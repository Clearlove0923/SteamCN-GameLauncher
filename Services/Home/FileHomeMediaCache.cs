using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>按游戏目录下载并校验首页媒体，失败时保留远程 URL 回退。</summary>
public sealed class FileHomeMediaCache : IHomeMediaCache
{
    private readonly string _mediaRoot;
    private readonly HttpClient _httpClient;
    private readonly HomeCachePolicy _policy;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _downloads = new(StringComparer.Ordinal);

    public FileHomeMediaCache(string mediaRoot, HttpClient httpClient, HomeCachePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaRoot);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(policy);
        _mediaRoot = mediaRoot;
        _httpClient = httpClient;
        _policy = policy;
    }

    public HomeContentResult ResolveAvailable(HomeContentRequest request, HomeContentResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var content = result.Content;
        var background = content.Background;
        if (background is not null)
        {
            var variants = background.Variants.Select(item => item with
            {
                ImageUrl = ResolveUri(request, item.ImageUrl, MediaKind.Image),
                LocalPath = ResolvePath(request, item.VideoUrl, MediaKind.Video) ?? item.LocalPath,
            }).ToArray();
            background = background with
            {
                // 保留远程 URL 作为播放身份；下载结束仅更新本地路径，不重播正在展示的动画。
                LocalPath = ResolvePath(request, background.VideoUrl, MediaKind.Video) ?? background.LocalPath,
                ImageUrl = ResolveUri(request, background.ImageUrl, MediaKind.Image),
                Variants = variants,
            };
        }

        var banners = content.Banners.Select(item => item with
        {
            LocalPath = ResolvePath(request, item.ImageUrl, MediaKind.Image) ?? item.LocalPath,
        }).ToArray();
        var news = content.News.Select(item => item with
        {
            ImageUrl = ResolveUri(request, item.ImageUrl, MediaKind.Image),
        }).ToArray();

        return result with
        {
            Content = content with { Background = background, Banners = banners, News = news },
        };
    }

    public async Task<HomeContentResult> CacheAsync(
        HomeContentRequest request,
        HomeContentResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var candidates = new HashSet<(string Url, MediaKind Kind)>();
        Add(candidates, result.Content.Background?.VideoUrl, MediaKind.Video);
        Add(candidates, result.Content.Background?.ImageUrl, MediaKind.Image);
        foreach (var variant in result.Content.Background?.Variants ?? [])
        {
            Add(candidates, variant.VideoUrl, MediaKind.Video);
            Add(candidates, variant.ImageUrl, MediaKind.Image);
        }
        foreach (var banner in result.Content.Banners) Add(candidates, banner.ImageUrl, MediaKind.Image);
        foreach (var item in result.Content.News) Add(candidates, item.ImageUrl, MediaKind.Image);

        using var throttle = new SemaphoreSlim(3, 3);
        // 限制并发下载数量，避免大视频与轮播图片同时抢占网络和磁盘。
        await Task.WhenAll(candidates.Select(async candidate =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await EnsureCachedAsync(request, candidate.Url, candidate.Kind, cancellationToken).ConfigureAwait(false); }
            finally { throttle.Release(); }
        })).ConfigureAwait(false);
        return ResolveAvailable(request, result);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var directory in MediaDirectories()) Directory.Delete(directory, recursive: true);
    }, cancellationToken);

    public Task CleanupAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var now = DateTime.UtcNow;
        var files = MediaDirectories().SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastAccessTimeUtc)
            .ToList();

        foreach (var file in files.Where(file => now - file.LastAccessTimeUtc > _policy.Retention))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(file.FullName);
        }

        files = files.Where(file => file.Exists).OrderByDescending(file => file.LastAccessTimeUtc).ToList();
        long retained = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retained + file.Length <= _policy.MaximumMediaBytes)
                retained += file.Length;
            else
                TryDelete(file.FullName);
        }
    }, cancellationToken);

    private async Task<string?> EnsureCachedAsync(
        HomeContentRequest request,
        string url,
        MediaKind kind,
        CancellationToken cancellationToken)
    {
        var existing = FindExisting(request, url, kind);
        if (existing is not null) return existing;
        var folder = HomeCacheKey.FolderSegment(request);
        var key = $"{folder}:{kind}:{url}";
        var lazy = _downloads.GetOrAdd(key, _ => new Lazy<Task<string?>>(
            () => DownloadAsync(request, url, kind, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try { return await lazy.Value.ConfigureAwait(false); }
        finally { _downloads.TryRemove(key, out _); }
    }

    private async Task<string?> DownloadAsync(
        HomeContentRequest request,
        string url,
        MediaKind kind,
        CancellationToken cancellationToken)
    {
        if (!TryGetHttpsUri(url, out var uri)) return null;
        try
        {
            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, uri),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var extension = ExtensionFor(mime, kind);
            if (extension is null) return null;
            var maximumBytes = kind == MediaKind.Video ? _policy.MaximumVideoBytes : _policy.MaximumImageBytes;
            if (response.Content.Headers.ContentLength is long length && length > maximumBytes) return null;

            var directory = GetMediaDirectory(request, kind);
            Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
            // 使用 URL 哈希命名，避免远程文件名冲突或把不可信文件名写入缓存目录。
            var destination = Path.Combine(directory, hash + extension);
            var temporary = destination + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += count;
                        if (total > maximumBytes) throw new InvalidDataException("首页媒体文件超过缓存大小限制。");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    }
                }
                if (!HasExpectedSignature(temporary, kind, extension))
                    // 仅凭响应的 MIME 不足以信任文件，写入正式缓存前还要校验文件头。
                    throw new InvalidDataException("首页媒体文件内容与响应类型不匹配。");
                File.Move(temporary, destination, overwrite: true);
                return destination;
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            LogService.Instance.AddLog($"[首页缓存] 媒体缓存失败，继续使用远程资源：{ex.Message}");
            return null;
        }
    }

    private string ResolveUri(HomeContentRequest request, string? source, MediaKind kind)
    {
        var path = ResolvePath(request, source, kind);
        return path is null ? source ?? string.Empty : new Uri(path, UriKind.Absolute).AbsoluteUri;
    }

    private string? ResolvePath(HomeContentRequest request, string? source, MediaKind kind)
    {
        if (!TryGetHttpsUri(source, out _)) return null;
        var path = FindExisting(request, source!, kind);
        if (path is null) return null;
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return path;
    }

    private string? FindExisting(HomeContentRequest request, string url, MediaKind kind)
    {
        if (!TryGetHttpsUri(url, out _)) return null;
        var directory = GetMediaDirectory(request, kind);
        if (!Directory.Exists(directory)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Directory.EnumerateFiles(directory, hash + ".*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private string GetMediaDirectory(HomeContentRequest request, MediaKind kind) => Path.Combine(
        _mediaRoot,
        HomeCacheKey.FolderSegment(request),
        "media",
        kind == MediaKind.Video ? "videos" : "images");

    private IEnumerable<string> MediaDirectories() => Directory.Exists(_mediaRoot)
        ? Directory.EnumerateDirectories(_mediaRoot)
            .Select(gameDirectory => Path.Combine(gameDirectory, "media"))
            .Where(Directory.Exists)
        : [];

    private static void Add(HashSet<(string Url, MediaKind Kind)> set, string? source, MediaKind kind)
    {
        if (TryGetHttpsUri(source, out _)) set.Add((source!, kind));
    }

    private static bool TryGetHttpsUri(string? source, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(source, UriKind.Absolute, out var parsed)
            && parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && (uri = parsed) is not null;
    }

    private static string? ExtensionFor(string? mime, MediaKind kind) => (kind, mime) switch
    {
        (MediaKind.Video, "video/mp4") => ".mp4",
        (MediaKind.Video, "video/webm") => ".webm",
        (MediaKind.Image, "image/jpeg") => ".jpg",
        (MediaKind.Image, "image/png") => ".png",
        (MediaKind.Image, "image/webp") => ".webp",
        (MediaKind.Image, "image/gif") => ".gif",
        _ => null,
    };

    private static bool HasExpectedSignature(string path, MediaKind kind, string extension)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var length = stream.Read(header);
        if (length < 4) return false;
        return extension switch
        {
            ".jpg" => header[0] == 0xFF && header[1] == 0xD8,
            ".png" => header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => length >= 12 && Encoding.ASCII.GetString(header[..4]) == "RIFF"
                && Encoding.ASCII.GetString(header.Slice(8, 4)) == "WEBP",
            ".gif" => Encoding.ASCII.GetString(header[..4]) == "GIF8",
            ".mp4" => length >= 12 && Encoding.ASCII.GetString(header.Slice(4, 4)) == "ftyp",
            ".webm" => header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            _ => false,
        };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private enum MediaKind { Image, Video }
}
