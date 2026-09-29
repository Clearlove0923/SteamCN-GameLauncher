using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>Kachina 更新源标识和手动下载地址。</summary>
public static class UpdateSourcePolicy
{
    public const string GitHubRepositoryUrl =
        "https://github.com/Clearlove0923/SteamCN-GameLauncher";

    // CNB 仓库创建后必须保持此路径；公开 Release 附件无需在客户端携带 Token。
    public const string CnbRepositoryPath = "SteamCN-GameLauncher/SteamCN-GameLauncher";
    public const string CnbRepositoryUrl = "https://cnb.cool/" + CnbRepositoryPath;

    public static string Normalize(string? sourceId) => UpdateSourceIds.Normalize(sourceId);

    public static string GetDisplayName(string? sourceId) =>
        Normalize(sourceId) == UpdateSourceIds.Cnb ? "CNB（国内推荐）" : "GitHub";

    public static string GetManualDownloadUrl(string? sourceId) =>
        Normalize(sourceId) == UpdateSourceIds.Cnb
            ? CnbRepositoryUrl + "/-/releases/latest"
            : GitHubRepositoryUrl + "/releases/latest";

}
