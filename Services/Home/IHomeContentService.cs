using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>页面使用的首页入口；负责缓存、降级和请求去重。</summary>
public interface IHomeContentService
{
    Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}
