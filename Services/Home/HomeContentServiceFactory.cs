using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>按设置创建可替换的传输、缓存和媒体服务，并统一决定缓存根目录。</summary>
public sealed class HomeContentServiceFactory
{
    public static HomeContentServiceFactory Instance { get; } = new();
    public static string PreferredCacheRoot { get; } = Path.Combine(AppContext.BaseDirectory, "HomeCache");
    public static string CacheRoot { get; } = ResolveWritableCacheRoot();
    public static HomeVideoVariantSelector VideoSelector { get; } = new(CacheRoot);
    public static string MetadataCacheRoot => CacheRoot;
    public static string MediaCacheRoot => CacheRoot;

    private static string ResolveWritableCacheRoot()
    {
        // 优先满足便携部署：媒体和元数据都位于软件安装目录的 HomeCache。
        // 安装在受保护目录时才整体回退到当前用户的 LocalAppData。
        try
        {
            Directory.CreateDirectory(PreferredCacheRoot);
            var probe = Path.Combine(PreferredCacheRoot, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return PreferredCacheRoot;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamCN-GameLauncher", "Cache", "Home");
            LogService.Instance.AddLog($"[首页缓存] 软件目录不可写，改用用户缓存目录：{fallback}");
            return fallback;
        }
    }

    private readonly object _sync = new();
    private string _configurationKey = "";
    private IHomeContentService? _service;
    private FileHomeContentCache? _contentCache;
    private FileHomeMediaCache? _mediaCache;

    private HomeContentServiceFactory() { }

    public IHomeContentService GetOrCreate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var baseUrl = settings.HomeContentWorkerBaseUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl)) return new PreviewHomeContentService();

        var maximumMegabytes = Math.Clamp(settings.HomeCacheMaximumMegabytes, 128, 4096);
        var retentionDays = Math.Clamp(settings.HomeCacheRetentionDays, 1, 90);
        var key = $"{baseUrl}|{Math.Max(1, settings.HomeContentWorkerTimeoutSeconds)}|{maximumMegabytes}|{retentionDays}";
        lock (_sync)
        {
            // 连接参数及缓存策略未变时复用服务，避免每次切换游戏都创建 HTTP 客户端。
            if (_service is not null && _configurationKey == key) return _service;
            try
            {
                var endpoint = new Uri(new Uri(baseUrl), "/v1/home-content");
                var transportClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.HomeContentWorkerTimeoutSeconds)),
                };
                var mediaClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var policy = new HomeCachePolicy
                {
                    MaximumMediaBytes = maximumMegabytes * 1024L * 1024,
                    Retention = TimeSpan.FromDays(retentionDays),
                };
                _contentCache = new FileHomeContentCache(MetadataCacheRoot);
                _mediaCache = new FileHomeMediaCache(MediaCacheRoot, mediaClient, policy);
                _service = new CachedHomeContentService(
                    new FastApiHomeContentService(new HttpHomeContentTransport(transportClient, endpoint)),
                    _contentCache,
                    _mediaCache,
                    policy);
                _configurationKey = key;
                return _service;
            }
            catch (UriFormatException)
            {
                return new PreviewHomeContentService();
            }
        }
    }

    public async Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() =>
        {
            return FileHomeContentCache.DirectorySize(CacheRoot, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        // 先要求服务取消下载和刷新，再删除游戏目录，避免后台任务复活旧缓存。
        CachedHomeContentService? cached;
        lock (_sync) cached = _service as CachedHomeContentService;
        if (cached is not null)
        {
            await cached.ClearAsync(cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(CacheRoot)) Directory.Delete(CacheRoot, recursive: true);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(CacheRoot)) Directory.Delete(CacheRoot, recursive: true);
        }, cancellationToken).ConfigureAwait(false);
    }
}
