namespace SteamCNGameLauncher;

/// <summary>
/// 应用版本信息的单一真相来源。
/// 修改此处即可同步更新所有界面中显示的版本文字。
/// </summary>
public static class AppInfo
{
    /// <summary>SemVer 版本号，如 3.0.0（不带 v 前缀，供 Kachina/GitHub Release 直接解析）</summary>
    public const string Version = "3.1.4";

    /// <summary>发布渠道/阶段，如 Alpha 1 Test、Beta、Release</summary>
    public const string Channel = "Release";

    /// <summary>完整版本字符串，用于界面显示，如 "v3.1.3 (Release)"</summary>
    public const string FullVersion = $"v{Version} ({Channel})";

    /// <summary>窗口标题</summary>
    public const string WindowTitle = $"{AppName} {FullVersion}";

    /// <summary>应用名称</summary>
    public const string AppName = "Steam国服游戏启动器";

    /// <summary>版权信息</summary>
    public const string Copyright = "© 2026 Violet0923";
}
