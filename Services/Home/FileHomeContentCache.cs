using System.Text.Json;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>按游戏目录保存规范化首页 JSON，并保留上次成功快照。</summary>
public sealed class FileHomeContentCache : IHomeContentCache
{
    private readonly string _cacheRoot;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public FileHomeContentCache(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = cacheRoot;
    }

    public async Task<HomeContentCacheEntry?> ReadAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(request);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<HomeContentCacheEntry>(json, HomeContentJson.Options);
            if (entry?.Result?.Content is null) return null;
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return entry;
        }
        catch (JsonException)
        {
            // 损坏的缓存不能反序列化为首页内容，删除后交由上层重新获取。
            TryDelete(path);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task WriteAsync(
        HomeContentRequest request,
        HomeContentResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var path = GetPath(request);
        var temporary = path + ".tmp";
        var previous = path + ".previous.json";
        var json = JsonSerializer.Serialize(
            new HomeContentCacheEntry(DateTimeOffset.UtcNow, result with { IsStale = false }),
            HomeContentJson.Options);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            // 临时文件写完后再替换正式文件；旧版另外保留为排查和恢复依据。
            if (File.Exists(path)) File.Copy(path, previous, overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
            _ioLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var directory in MetadataDirectories())
                Directory.Delete(directory, recursive: true);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task CleanupAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cutoff = DateTime.UtcNow - retention;
            foreach (var directory in MetadataDirectories())
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (File.GetLastAccessTimeUtc(path) < cutoff) File.Delete(path); }
                catch (IOException) { }
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task<long> GetSizeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => MetadataDirectories().Sum(directory => DirectorySize(directory, cancellationToken)), cancellationToken);

    private IEnumerable<string> MetadataDirectories() => Directory.Exists(_cacheRoot)
        ? Directory.EnumerateDirectories(_cacheRoot)
            .Select(gameDirectory => Path.Combine(gameDirectory, "metadata"))
            .Where(Directory.Exists)
        : [];

    private string GetPath(HomeContentRequest request)
    {
        var provider = HomeCacheKey.SafeSegment(request.ProviderId, "provider");
        var game = HomeCacheKey.SafeSegment(request.GameId, "game");
        var locale = HomeCacheKey.SafeSegment(request.Locale, "locale");
        return Path.Combine(_cacheRoot, HomeCacheKey.FolderSegment(request), "metadata",
            provider, game, $"{locale}-{HomeCacheKey.Create(request)}.json");
    }

    internal static long DirectorySize(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return 0;
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { total += new FileInfo(path).Length; } catch (IOException) { }
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
