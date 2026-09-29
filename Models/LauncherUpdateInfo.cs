namespace SteamCNGameLauncher.Models;

/// <summary>启动器自身的软件更新提示，不与游戏首页的 UpdateInfo 混用。</summary>
public sealed record LauncherUpdateInfo(
    string Version,
    string ReleaseNotes,
    string DownloadUrl);
