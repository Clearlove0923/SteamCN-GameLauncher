namespace SteamCNGameLauncher.Models.Home;

/// <summary>
/// 描述不同内容来源在统一首页中的可变区域，不包含游戏或厂商判断。
/// 布局尺寸使用截图实际像素，页面会根据显示缩放率转换为 WinUI 逻辑像素。
/// </summary>
public sealed record HomeLayoutProfile(
    string Id,
    double NewsWidth,
    double NewsHeight,
    double NewsHeroHeight,
    double NewsLeft,
    double NewsBottom,
    double LaunchRight,
    double LaunchBottom,
    double StartButtonWidth,
    double StartButtonHeight,
    double LaunchSettingsButtonSize,
    double SteamLaunchOptionsWidth,
    double SteamLaunchOptionsHeight)
{
    public const string MihoyoLauncherId = "mihoyo-launcher";
}
