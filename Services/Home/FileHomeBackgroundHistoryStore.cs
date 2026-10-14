using System.Text.Json;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>独立于媒体缓存，按游戏保存最后一次通过下载校验的远程背景来源。</summary>
public sealed class FileHomeBackgroundHistoryStore : IHomeBackgroundHistoryStore
{
    private readonly string _stateRoot;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public FileHomeBackgroundHistoryStore(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        _stateRoot = stateRoot;
    }

    public async Task<HomeBackground?> ReadAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(request);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<BackgroundHistoryEntry>(json, HomeContentJson.Options);
            return Normalize(entry?.Background);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
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
        HomeBackground background,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(background);
        if (normalized is null) return;
        var path = GetPath(request);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(
            new BackgroundHistoryEntry(DateTimeOffset.UtcNow, normalized),
            HomeContentJson.Options);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            _ioLock.Release();
        }
    }

    private string GetPath(HomeContentRequest request)
    {
        var game = HomeCacheKey.SafeSegment(request.GameId, "game");
        var provider = HomeCacheKey.SafeSegment(request.ProviderId, "provider");
        var locale = HomeCacheKey.SafeSegment(request.Locale, "locale");
        return Path.Combine(_stateRoot, "backgrounds", game, provider,
            $"{locale}-{HomeCacheKey.CreateBackgroundHistoryKey(request)}.json");
    }

    private static HomeBackground? Normalize(HomeBackground? background)
    {
        if (background is null) return null;
        var variants = background.Variants
            .Where(item => IsHttps(item.VideoUrl))
            .Select(item => item with
            {
                ImageUrl = IsHttps(item.ImageUrl) ? item.ImageUrl : null,
                LocalPath = null,
            })
            .ToArray();
        var videoUrl = IsHttps(background.VideoUrl) ? background.VideoUrl : null;
        if (videoUrl is null && variants.Length == 0) return null;
        return background with
        {
            VideoUrl = videoUrl,
            ImageUrl = IsHttps(background.ImageUrl) ? background.ImageUrl : null,
            LocalPath = null,
            Variants = variants,
        };
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private sealed record BackgroundHistoryEntry(DateTimeOffset SavedAt, HomeBackground Background);
}
