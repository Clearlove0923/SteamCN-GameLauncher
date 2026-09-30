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

    public static string GetPackageUrl(string? sourceId, string tag)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(tag,
                @"^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("发布版本号格式无效。");
        var version = tag.TrimStart('v');
        var prefix = Normalize(sourceId) == UpdateSourceIds.Cnb
            ? CnbRepositoryUrl + "/-/releases/download/"
            : GitHubRepositoryUrl + "/releases/download/";
        return prefix + Uri.EscapeDataString(tag) + "/SteamCN-GameLauncher.Install." + version + ".exe";
    }

}
