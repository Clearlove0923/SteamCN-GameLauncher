using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class LauncherHomePage : Page
{
    private readonly HomeLayoutProfileCatalog _layoutProfiles = new();
    private string? _activeLayoutProfileId;

    public LauncherHomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyLayoutProfile(_activeLayoutProfileId);
        // 窗口跨显示器或缩放率变化时，保持资讯栏的截图实际像素尺寸不变。
        SizeChanged += (_, _) => ApplyLayoutProfile(_activeLayoutProfileId);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var id = e.Parameter as string ?? CustomManifestService.Instance.GetInitialSidebarId();
        if (!string.IsNullOrWhiteSpace(id) && CustomManifestService.Instance.GetById(id) is { } game)
            ShowGame(game);
    }

    private void ShowGame(Models.CustomManifestPreset game)
    {
        ApplyLayoutProfile(game.HomeLayoutProfileId);
        GameTitle.Text = game.Name;
        GameSubtitle.Text = string.IsNullOrWhiteSpace(game.GameDisplayName)
            ? "启动方式、背景和资讯入口将在后续版本接入。"
            : game.GameDisplayName;
    }

    private void ApplyLayoutProfile(string? profileId)
    {
        _activeLayoutProfileId = profileId;
        var profile = _layoutProfiles.Resolve(profileId);
        var rasterizationScale = XamlRoot?.RasterizationScale ?? 1d;
        NewsPanel.Width = profile.NewsWidth / rasterizationScale;
        NewsPanel.Height = profile.NewsHeight / rasterizationScale;
        NewsPanel.Margin = new Microsoft.UI.Xaml.Thickness(
            profile.NewsLeft / rasterizationScale, 0, 0, profile.NewsBottom / rasterizationScale);
        NewsHeroRow.Height = new Microsoft.UI.Xaml.GridLength(profile.NewsHeroHeight / rasterizationScale);
        LaunchDock.Margin = new Microsoft.UI.Xaml.Thickness(
            0, 0, profile.LaunchRight / rasterizationScale, profile.LaunchBottom / rasterizationScale);
        LaunchDock.Spacing = 10 / rasterizationScale;
        LaunchButtonRow.Spacing = 12 / rasterizationScale;

        StartGameButton.Width = profile.StartButtonWidth / rasterizationScale;
        StartGameButton.Height = profile.StartButtonHeight / rasterizationScale;
        StartGameButton.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(
            profile.StartButtonHeight / 2 / rasterizationScale);
        StartGameButtonContent.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        StartGameButtonContent.RenderTransform = CreateDisplayScaleTransform(rasterizationScale);

        LaunchSettingsButton.Width = LaunchSettingsButton.Height =
            profile.LaunchSettingsButtonSize / rasterizationScale;
        LaunchSettingsButton.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(
            profile.LaunchSettingsButtonSize / 2 / rasterizationScale);
        LaunchSettingsIcon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        LaunchSettingsIcon.RenderTransform = CreateDisplayScaleTransform(rasterizationScale);

        SteamLaunchOptions.Width = profile.SteamLaunchOptionsWidth / rasterizationScale;
        SteamLaunchOptions.Height = profile.SteamLaunchOptionsHeight / rasterizationScale;
        SteamLaunchOptions.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(22 / rasterizationScale);
        SteamLaunchOptions.Padding = new Microsoft.UI.Xaml.Thickness(
            16 / rasterizationScale, 8 / rasterizationScale,
            16 / rasterizationScale, 8 / rasterizationScale);
        SteamLaunchOptionsContent.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        SteamLaunchOptionsContent.RenderTransform = CreateDisplayScaleTransform(rasterizationScale);
    }

    private static ScaleTransform CreateDisplayScaleTransform(double rasterizationScale) => new()
    {
        ScaleX = 1 / rasterizationScale,
        ScaleY = 1 / rasterizationScale
    };
}
