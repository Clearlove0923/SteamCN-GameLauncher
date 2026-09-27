using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

public sealed record HomeCachePolicy
{
    public TimeSpan MemoryLifetime { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MetadataLifetime { get; init; } = TimeSpan.FromMinutes(20);
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);
    public long MaximumMediaBytes { get; init; } = 1024L * 1024 * 1024;
    public long MaximumImageBytes { get; init; } = 20L * 1024 * 1024;
    public long MaximumVideoBytes { get; init; } = 300L * 1024 * 1024;
}

public sealed record HomeContentCacheEntry(
    DateTimeOffset CachedAt,
    HomeContentResult Result);

public interface IHomeContentCache
{
    Task<HomeContentCacheEntry?> ReadAsync(HomeContentRequest request, CancellationToken cancellationToken = default);
    Task WriteAsync(HomeContentRequest request, HomeContentResult result, CancellationToken cancellationToken = default);
    Task CleanupAsync(TimeSpan retention, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<long> GetSizeAsync(CancellationToken cancellationToken = default);
}

public interface IHomeMediaCache
{
    HomeContentResult ResolveAvailable(HomeContentRequest request, HomeContentResult result);
    Task<HomeContentResult> CacheAsync(
        HomeContentRequest request,
        HomeContentResult result,
        CancellationToken cancellationToken = default);
    Task CleanupAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IHomeContentRefreshSource
{
    event EventHandler<HomeContentRefreshedEventArgs>? ContentRefreshed;
}

public sealed class HomeContentRefreshedEventArgs(
    HomeContentRequest request,
    HomeContentResult result) : EventArgs
{
    public HomeContentRequest Request { get; } = request;
    public HomeContentResult Result { get; } = result;
}
