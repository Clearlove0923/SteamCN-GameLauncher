using System.Collections.Concurrent;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// 统一首页缓存层：先读内存和磁盘，过期内容仍可立即展示，同时在后台刷新。
/// 页面只接收统一 DTO；Provider 请求、媒体下载和降级状态都在此层协调。
/// </summary>
public sealed class CachedHomeContentService : IHomeContentService, IHomeContentRefreshSource
{
    private readonly IHomeContentService _inner;
    private readonly IHomeContentCache _contentCache;
    private readonly IHomeMediaCache _mediaCache;
    private readonly IHomeBackgroundHistoryStore _backgroundHistory;
    private readonly HomeCachePolicy _policy;
    private readonly ConcurrentDictionary<string, MemoryEntry> _memory = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<HomeContentResult>>> _refreshes = new(StringComparer.Ordinal);
    private CancellationTokenSource _lifetime = new();

    public event EventHandler<HomeContentRefreshedEventArgs>? ContentRefreshed;

    public CachedHomeContentService(
        IHomeContentService inner,
        IHomeContentCache contentCache,
        IHomeMediaCache mediaCache,
        IHomeBackgroundHistoryStore backgroundHistory,
        HomeCachePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(contentCache);
        ArgumentNullException.ThrowIfNull(mediaCache);
        ArgumentNullException.ThrowIfNull(backgroundHistory);
        ArgumentNullException.ThrowIfNull(policy);
        _inner = inner;
        _contentCache = contentCache;
        _mediaCache = mediaCache;
        _backgroundHistory = backgroundHistory;
        _policy = policy;
    }

    public async Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = HomeCacheKey.Create(request);
        if (_memory.TryGetValue(key, out var memory)
            && DateTimeOffset.UtcNow - memory.StoredAt <= _policy.MemoryLifetime)
        {
            var expired = DateTimeOffset.UtcNow - memory.Entry.CachedAt > _policy.MetadataLifetime;
            // 命中旧数据时先返回可用内容，不让用户等待网络；同键后台刷新会去重。
            if (expired) ScheduleRefresh(key, request);
            return _mediaCache.ResolveAvailable(request, memory.Entry.Result) with { IsStale = expired };
        }

        var cached = await _contentCache.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _memory[key] = new MemoryEntry(DateTimeOffset.UtcNow, cached);
            var expired = DateTimeOffset.UtcNow - cached.CachedAt > _policy.MetadataLifetime;
            var resolved = _mediaCache.ResolveAvailable(request, cached.Result) with { IsStale = expired };
            await RememberValidatedBackgroundAsync(request, cached.Result, resolved, cancellationToken)
                .ConfigureAwait(false);
            // 每个新进程首次从磁盘读取该游戏时都检查一次 Provider。先返回缓存，
            // 所以不会延迟首页；刷新失败时再补齐旧媒体，避免旧下载晚到后覆盖新内容。
            ScheduleStartupRefresh(key, request, cached.Result);
            return resolved;
        }

        return await GetOrStartRefresh(key, request)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // 先取消旧请求，再清理缓存，避免清理完成后旧下载又把文件写回来。
        var previous = Interlocked.Exchange(ref _lifetime, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
        _memory.Clear();
        _refreshes.Clear();
        await _contentCache.ClearAsync(cancellationToken).ConfigureAwait(false);
        await _mediaCache.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<HomeContentResult> GetOrStartRefresh(string key, HomeContentRequest request)
    {
        // Lazy<Task> 防止并发点击同一游戏时启动多次相同 Provider 请求。
        var lazy = _refreshes.GetOrAdd(key, _ => new Lazy<Task<HomeContentResult>>(
            () => RefreshCoreAsync(request, _lifetime.Token),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndReleaseAsync(key, lazy);
    }

    private async Task<HomeContentResult> AwaitAndReleaseAsync(
        string key,
        Lazy<Task<HomeContentResult>> refresh)
    {
        try { return await refresh.Value.ConfigureAwait(false); }
        finally { _refreshes.TryRemove(new KeyValuePair<string, Lazy<Task<HomeContentResult>>>(key, refresh)); }
    }

    private void ScheduleRefresh(string key, HomeContentRequest request) =>
        _ = ObserveAsync(GetOrStartRefresh(key, request), "元数据后台刷新");

    private void ScheduleStartupRefresh(
        string key,
        HomeContentRequest request,
        HomeContentResult cachedResult) =>
        _ = ObserveAsync(RefreshOrCacheExistingMediaAsync(key, request, cachedResult), "启动期元数据后台刷新");

    private async Task RefreshOrCacheExistingMediaAsync(
        string key,
        HomeContentRequest request,
        HomeContentResult cachedResult)
    {
        var refreshed = await GetOrStartRefresh(key, request).ConfigureAwait(false);
        if (!HasUsableContent(refreshed.Content))
            await CacheMediaAndPublishAsync(request, cachedResult, _lifetime.Token).ConfigureAwait(false);
    }

    private async Task<HomeContentResult> RefreshCoreAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _inner.GetAsync(request, cancellationToken).ConfigureAwait(false);
        // 全空或失败响应不覆盖最后一次成功缓存。
        if (!HasUsableContent(result.Content)) return result;

        // 厂商会在活动切换期间返回有效的 Banner/资讯，但暂时不下发首页动画。
        // 这仍是一个“部分成功”响应，不能用 null background 覆盖已验证的旧背景。
        // 仅补齐缺失的顶层背景；新响应一旦提供背景，始终以新数据为准。
        var key = HomeCacheKey.Create(request);
        if (result.Content.Background is null)
        {
            HomeBackground? fallbackBackground = null;
            if (_memory.TryGetValue(key, out var previous))
                fallbackBackground = previous.Entry.Result.Content.Background;
            if (fallbackBackground is null)
            {
                fallbackBackground = await _backgroundHistory.ReadAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (fallbackBackground is not null)
                    LogService.Instance.AddLog("[首页缓存] 新内容未下发背景，已从独立历史索引恢复动画来源");
            }
            else
            {
                LogService.Instance.AddLog("[首页缓存] 新内容未下发背景，已保留上次成功动画");
            }
            if (fallbackBackground is not null)
                result = result with
                {
                    Content = result.Content with { Background = fallbackBackground },
                };
        }

        await _contentCache.WriteAsync(request, result, cancellationToken).ConfigureAwait(false);
        await _contentCache.CleanupAsync(_policy.Retention, cancellationToken).ConfigureAwait(false);
        var entry = new HomeContentCacheEntry(DateTimeOffset.UtcNow, result with { IsStale = false });
        _memory[key] = new MemoryEntry(DateTimeOffset.UtcNow, entry);
        var resolved = _mediaCache.ResolveAvailable(request, result) with { IsStale = false };
        ContentRefreshed?.Invoke(this, new HomeContentRefreshedEventArgs(request, resolved));
        ScheduleMediaCaching(request, result);
        return resolved;
    }

    private void ScheduleMediaCaching(HomeContentRequest request, HomeContentResult rawResult) =>
        _ = ObserveAsync(CacheMediaAndPublishAsync(request, rawResult, _lifetime.Token), "媒体后台缓存");

    private async Task CacheMediaAndPublishAsync(
        HomeContentRequest request,
        HomeContentResult rawResult,
        CancellationToken cancellationToken)
    {
        var localized = await _mediaCache.CacheAsync(request, rawResult, cancellationToken).ConfigureAwait(false);
        await RememberValidatedBackgroundAsync(request, rawResult, localized, cancellationToken)
            .ConfigureAwait(false);
        await _mediaCache.CleanupAsync(cancellationToken).ConfigureAwait(false);
        ContentRefreshed?.Invoke(this, new HomeContentRefreshedEventArgs(request, localized));
    }

    private static async Task ObserveAsync(Task task, string operation)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogService.Instance.AddLog($"[首页缓存] {operation}失败：{ex.Message}"); }
    }

    private static bool HasUsableContent(HomeContent content) =>
        content.Background is not null
        || content.Banners.Count > 0
        || content.News.Count > 0
        || content.UpdateInfo is not null;

    private async Task RememberValidatedBackgroundAsync(
        HomeContentRequest request,
        HomeContentResult rawResult,
        HomeContentResult localizedResult,
        CancellationToken cancellationToken)
    {
        var raw = rawResult.Content.Background;
        var localized = localizedResult.Content.Background;
        if (raw is null || localized is null) return;

        var validatedVariants = raw.Variants
            .Select(item => (Raw: item, Localized: localized.Variants.FirstOrDefault(local => local.Id == item.Id)))
            .Where(item => IsDownloadedRemoteMedia(item.Raw.LocalPath, item.Localized?.LocalPath))
            .Select(item => item.Raw with
            {
                ImageUrl = HasValidatedLocalUri(item.Localized?.ImageUrl) ? item.Raw.ImageUrl : null,
            })
            .ToArray();
        var validatedMain = !string.IsNullOrWhiteSpace(raw.VideoUrl)
            && IsDownloadedRemoteMedia(raw.LocalPath, localized.LocalPath);
        if (!validatedMain && validatedVariants.Length == 0) return;

        await _backgroundHistory.WriteAsync(request, raw with
        {
            VideoUrl = validatedMain ? raw.VideoUrl : null,
            ImageUrl = HasValidatedLocalUri(localized.ImageUrl) ? raw.ImageUrl : null,
            LocalPath = null,
            Variants = validatedVariants,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasValidatedLocalUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.IsFile
        && File.Exists(uri.LocalPath);

    private static bool IsDownloadedRemoteMedia(string? providerLocalPath, string? localizedPath)
    {
        if (string.IsNullOrWhiteSpace(localizedPath)) return false;
        if (string.IsNullOrWhiteSpace(providerLocalPath)) return true;
        try
        {
            var providerPath = Uri.TryCreate(providerLocalPath, UriKind.Absolute, out var providerUri)
                && providerUri.IsFile
                    ? providerUri.LocalPath
                    : providerLocalPath;
            return !Path.GetFullPath(providerPath).Equals(
                Path.GetFullPath(localizedPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private sealed record MemoryEntry(DateTimeOffset StoredAt, HomeContentCacheEntry Entry);
}
