using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services.Home;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

var root = Path.Combine(Path.GetTempPath(), $"home-cache-tests-{Guid.NewGuid():N}");
try
{
    var request = new HomeContentRequest
    {
        GameId = "4162040",
        ProviderId = "hoyoplay-json",
        Locale = "zh-CN",
    };
    var initial = Result("old-news", "https://cdn.example/background.mp4", "https://cdn.example/banner.png");
    var disk = new FileHomeContentCache(root);
    var media = new FakeMediaCache();
    var source = new FakeHomeContentService { Result = initial };
    var service = new CachedHomeContentService(source, disk, media, new HomeCachePolicy());

    var first = await service.GetAsync(request);
    Check(source.CallCount == 1 && first.Content.News.Single().Id == "old-news",
        "first request fetches provider and returns content");
    Check(await disk.GetSizeAsync() > 0, "successful response is persisted to metadata cache");

    var restartedSource = new FakeHomeContentService { Result = initial };
    var restarted = new CachedHomeContentService(restartedSource, disk, media, new HomeCachePolicy());
    var fromDisk = await restarted.GetAsync(request);
    Check(restartedSource.CallCount == 1 && fromDisk.Content.News.Single().Id == "old-news",
        "fresh disk cache is returned immediately while each new process checks the provider once");

    var refreshGate = new TaskCompletionSource<HomeContentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var refreshingSource = new FakeHomeContentService { PendingResult = refreshGate.Task };
    var staleService = new CachedHomeContentService(refreshingSource, disk, media,
        new HomeCachePolicy { MetadataLifetime = TimeSpan.Zero });
    var refreshedEvent = new TaskCompletionSource<HomeContentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    staleService.ContentRefreshed += (_, e) =>
    {
        if (e.Result.Content.News.Any(item => item.Id == "new-news")) refreshedEvent.TrySetResult(e.Result);
    };
    var stale = await staleService.GetAsync(request);
    Check(stale.IsStale && stale.Content.News.Single().Id == "old-news" && refreshingSource.CallCount == 1,
        "expired cache returns immediately while one background refresh starts");
    var staleAgain = await staleService.GetAsync(request);
    Check(staleAgain.IsStale && refreshingSource.CallCount == 1,
        "memory reuse preserves stale state and does not duplicate an active refresh");
    refreshGate.SetResult(Result("new-news", null, null));
    var refreshed = await refreshedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(refreshed.Content.News.Single().Id == "new-news",
        "background refresh publishes updated content without reopening page");

    var failingSource = new FakeHomeContentService
    {
        Result = new HomeContentResult(new HomeContent(), Errors:
        [
            new HomeContentError { Code = "offline", Message = "offline", Recoverable = true },
        ]),
    };
    var fallbackService = new CachedHomeContentService(failingSource, disk, media,
        new HomeCachePolicy { MetadataLifetime = TimeSpan.Zero });
    var fallback = await fallbackService.GetAsync(request);
    await Task.Delay(100);
    var preserved = await disk.ReadAsync(request);
    Check(fallback.IsStale && failingSource.CallCount == 1
        && preserved?.Result.Content.News.Single().Id == "new-news",
        "failed refresh keeps the last successful disk cache");

    var coalesceRoot = Path.Combine(root, "coalesce");
    var coalesceGate = new TaskCompletionSource<HomeContentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var coalesceSource = new FakeHomeContentService { PendingResult = coalesceGate.Task };
    var coalesced = new CachedHomeContentService(coalesceSource, new FileHomeContentCache(coalesceRoot), media,
        new HomeCachePolicy());
    var call1 = coalesced.GetAsync(request);
    var call2 = coalesced.GetAsync(request);
    await Task.Delay(50);
    Check(coalesceSource.CallCount == 1, "concurrent identical requests share one provider call");
    coalesceGate.SetResult(initial);
    await Task.WhenAll(call1, call2);

    var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 };
    var handler = new CountingHandler(png, "image/png");
    var realMedia = new FileHomeMediaCache(Path.Combine(root, "media-test"), new HttpClient(handler),
        new HomeCachePolicy());
    var mediaResult = Result("media", null, "https://cdn.example/banner.png");
    var localized = await realMedia.CacheAsync(request, mediaResult);
    var localPath = localized.Content.Banners.Single().LocalPath;
    Check(handler.CallCount == 1 && localPath is not null && File.Exists(localPath)
        && localPath.Contains(Path.Combine("4162040", "media", "images"), StringComparison.OrdinalIgnoreCase),
        "banner image is validated and stored under its game directory");
    Check(handler.LastUserAgent == $"SteamCN-GameLauncher/{SteamCNGameLauncher.AppInfo.Version}",
        "all homepage media downloads identify the current launcher version");
    await realMedia.CacheAsync(request, mediaResult);
    Check(handler.CallCount == 1, "existing media file is reused without another download");

    var otherGameRequest = request with { GameId = "3513350" };
    var otherLocalized = await realMedia.CacheAsync(otherGameRequest, mediaResult);
    Check(handler.CallCount == 2
        && otherLocalized.Content.Banners.Single().LocalPath is { } otherPath
        && otherPath.Contains(Path.Combine("3513350", "media", "images"), StringComparison.OrdinalIgnoreCase),
        "the same URL is isolated into a separate directory for each game");

    var providerVideo = Path.Combine(root, "provider-local", "bg.mp4");
    var providerBanner = Path.Combine(root, "provider-local", "banner.png");
    Directory.CreateDirectory(Path.GetDirectoryName(providerVideo)!);
    await File.WriteAllBytesAsync(providerVideo,
        [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0]);
    await File.WriteAllBytesAsync(providerBanner, png);
    var localPriorityHandler = new CountingHandler(png, "image/png");
    var localPriorityRoot = Path.Combine(root, "local-priority-cache");
    var localPriorityCache = new FileHomeMediaCache(localPriorityRoot, new HttpClient(localPriorityHandler),
        new HomeCachePolicy());
    var localPriorityResult = new HomeContentResult(new HomeContent
    {
        Background = new HomeBackground
        {
            VideoUrl = "https://cdn.example/older-background.mp4",
            LocalPath = providerVideo,
            ImageUrl = providerBanner,
            Variants =
            [
                new HomeVideoVariant
                {
                    Id = "local-variant",
                    VideoUrl = "https://cdn.example/older-variant.mp4",
                    LocalPath = providerVideo,
                },
            ],
        },
        Banners =
        [
            new HomeBanner
            {
                Id = "local-banner",
                ImageUrl = "https://cdn.example/older-banner.png",
                LocalPath = providerBanner,
            },
        ],
    });
    var localPriorityResolved = await localPriorityCache.CacheAsync(request, localPriorityResult);
    Check(localPriorityHandler.CallCount == 0
        && localPriorityResolved.Content.Background?.LocalPath == Path.GetFullPath(providerVideo)
        && localPriorityResolved.Content.Background?.Variants.Single().LocalPath == Path.GetFullPath(providerVideo)
        && localPriorityResolved.Content.Banners.Single().LocalPath == Path.GetFullPath(providerBanner),
        "valid provider local media wins and skips equivalent remote downloads");

    var olderRemoteUrl = localPriorityResult.Content.Background!.VideoUrl!;
    var remoteHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(olderRemoteUrl)));
    var remoteCacheDirectory = Path.Combine(localPriorityRoot, "4162040", "media", "videos");
    Directory.CreateDirectory(remoteCacheDirectory);
    await File.WriteAllBytesAsync(Path.Combine(remoteCacheDirectory, remoteHash + ".mp4"),
        [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0]);
    var localOverCachedRemote = localPriorityCache.ResolveAvailable(request, localPriorityResult);
    Check(localOverCachedRemote.Content.Background?.LocalPath == Path.GetFullPath(providerVideo),
        "an existing remote cache cannot replace a valid provider local background");

    File.Delete(providerVideo);
    var missingLocalHandler = new CountingHandler(
        [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0], "video/mp4");
    var missingLocalCache = new FileHomeMediaCache(Path.Combine(root, "missing-local-cache"),
        new HttpClient(missingLocalHandler), new HomeCachePolicy());
    var missingLocalResolved = await missingLocalCache.CacheAsync(request, localPriorityResult);
    Check(missingLocalHandler.CallCount == 2
        && missingLocalResolved.Content.Background?.LocalPath is { } fallbackVideo
        && fallbackVideo.Contains(Path.Combine("4162040", "media", "videos"), StringComparison.OrdinalIgnoreCase),
        "missing provider local videos fall back to downloading background and variant URLs");

    var namedFolderRequest = request with { CacheFolderName = "Genshin Impact Game" };
    var namedFolderResult = await realMedia.CacheAsync(namedFolderRequest, mediaResult);
    Check(namedFolderResult.Content.Banners.Single().LocalPath is { } namedPath
        && namedPath.Contains(Path.Combine("Genshin Impact Game", "media", "images"), StringComparison.OrdinalIgnoreCase),
        "media is grouped under the selected game folder name");

    var firstVideo = "https://cdn.example/character-a.webm";
    var secondVideo = "https://cdn.example/character-b.webm";
    var videoContent = new HomeContent
    {
        Background = new HomeBackground
        {
            VideoUrl = firstVideo,
            Variants =
            [
                new HomeVideoVariant { Id = "character-a", VideoUrl = firstVideo },
                new HomeVideoVariant { Id = "character-b", VideoUrl = secondVideo },
            ],
        },
    };
    var selectionRoot = Path.Combine(root, "selection");
    var firstRun = new HomeVideoVariantSelector(selectionRoot);
    var selectedFirst = firstRun.Select(namedFolderRequest, videoContent);
    Check(selectedFirst.Background?.VideoUrl == firstVideo
        && firstRun.Select(namedFolderRequest, videoContent).Background?.VideoUrl == firstVideo,
        "video choice remains stable while the app is running");
    var secondRun = new HomeVideoVariantSelector(selectionRoot);
    Check(secondRun.Select(namedFolderRequest, videoContent).Background?.VideoUrl == secondVideo,
        "the next app launch rotates to the other animation");

    var cachedVideoDir = Path.Combine(root, "media-test", "Genshin Impact Game", "media", "videos");
    Directory.CreateDirectory(cachedVideoDir);
    var videoHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secondVideo)));
    var cachedVideo = Path.Combine(cachedVideoDir, videoHash + ".webm");
    await File.WriteAllBytesAsync(cachedVideo, [0x1A, 0x45, 0xDF, 0xA3]);
    var selectedWithCache = realMedia.ResolveAvailable(namedFolderRequest,
        new HomeContentResult(videoContent));
    Check(selectedWithCache.Content.Background?.Variants[1].VideoUrl == secondVideo
        && selectedWithCache.Content.Background?.Variants[1].LocalPath == cachedVideo,
        "media cache keeps the stable provider URL alongside the local video path");

    var cleanupMedia = new FileHomeMediaCache(Path.Combine(root, "media-test"), new HttpClient(handler),
        new HomeCachePolicy { MaximumMediaBytes = 1 });
    await cleanupMedia.CleanupAsync();
    Check(localPath is not null && !File.Exists(localPath), "media cleanup enforces the configured capacity limit");

    await disk.WriteAsync(request, Result("third-news", null, null));
    Check(Directory.EnumerateFiles(Path.Combine(root, "4162040", "metadata"), "*.previous.json", SearchOption.AllDirectories).Any(),
        "metadata cache keeps one previous successful snapshot");

    foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "4162040", "metadata"), "*.json", SearchOption.AllDirectories))
        File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddDays(-40));
    await disk.CleanupAsync(TimeSpan.FromDays(30));
    Check(await disk.GetSizeAsync() == 0, "metadata older than the retention period is removed");

    await disk.WriteAsync(request, initial);
    await staleService.ClearAsync();
    Check(await disk.GetSizeAsync() == 0, "clear operation removes metadata cache and memory state");

    await disk.WriteAsync(namedFolderRequest, initial);
    Check(Directory.EnumerateFiles(Path.Combine(root, "Genshin Impact Game", "metadata"), "*.json",
        SearchOption.AllDirectories).Any(), "news metadata shares the game-named cache directory");
    await disk.ClearAsync();

    Console.WriteLine($"All {checks} checks passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static HomeContentResult Result(string newsId, string? videoUrl, string? bannerUrl) => new(new HomeContent
{
    Background = videoUrl is null ? null : new HomeBackground { VideoUrl = videoUrl },
    Banners = bannerUrl is null ? [] :
    [
        new HomeBanner { Id = "banner", ImageUrl = bannerUrl, TargetUrl = "https://example.invalid" },
    ],
    News =
    [
        new HomeNewsItem { Id = newsId, Title = newsId, Category = "资讯" },
    ],
});

sealed class FakeHomeContentService : IHomeContentService
{
    public int CallCount { get; private set; }
    public HomeContentResult Result { get; init; } = new(new HomeContent());
    public Task<HomeContentResult>? PendingResult { get; init; }

    public Task<HomeContentResult> GetAsync(HomeContentRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return PendingResult ?? Task.FromResult(Result);
    }
}

sealed class FakeMediaCache : IHomeMediaCache
{
    public HomeContentResult ResolveAvailable(HomeContentRequest request, HomeContentResult result) => result;
    public Task<HomeContentResult> CacheAsync(
        HomeContentRequest request,
        HomeContentResult result,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(result);
    public Task CleanupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class CountingHandler(byte[] payload, string mediaType) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public string? LastUserAgent { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastUserAgent = request.Headers.UserAgent.ToString();
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
