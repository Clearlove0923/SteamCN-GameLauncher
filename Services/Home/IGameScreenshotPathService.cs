using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>通过 Python 固定配置查询游戏截图目录；页面不保存游戏路径映射。</summary>
public interface IGameScreenshotPathService
{
    Task<GameScreenshotPathEnvelope> GetAsync(
        GameScreenshotPathRequest request,
        CancellationToken cancellationToken = default);
}
