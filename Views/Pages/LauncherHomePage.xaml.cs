using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class LauncherHomePage : Page
{
    private readonly HomeLayoutProfileCatalog _layoutProfiles = new();
    private readonly IHomeContentService _homeContentService = new PreviewHomeContentService();
    private readonly SettingsService _settingsService = new();
    private readonly SteamLaunchService _steamLaunchService = new();
    private readonly LogService _logService = LogService.Instance;
    private string? _activeLayoutProfileId;
    private CancellationTokenSource? _homeContentCancellation;
    private Models.CustomManifestPreset? _currentGame;

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
            _ = ShowGameAsync(game);
        else
        {
            _currentGame = null;
            StartGameButton.IsEnabled = false;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _homeContentCancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task ShowGameAsync(Models.CustomManifestPreset game)
    {
        _currentGame = game;
        StartGameButton.IsEnabled = true;
        ApplyLayoutProfile(game.HomeLayoutProfileId);
        GameTitle.Text = game.Name;
        GameSubtitle.Text = string.IsNullOrWhiteSpace(game.GameDisplayName)
            ? "启动方式、背景和资讯入口将在后续版本接入。"
            : game.GameDisplayName;

        _homeContentCancellation?.Cancel();
        _homeContentCancellation = new CancellationTokenSource();
        try
        {
            var result = await _homeContentService.GetAsync(new HomeContentRequest
            {
                GameId = game.Id,
                ProviderId = PreviewHomeContentService.ProviderId,
                Locale = "zh-CN"
            }, _homeContentCancellation.Token);
            HomeContentPanel.SetContent(result.Content);
        }
        catch (OperationCanceledException)
        {
            // 快速切换游戏时忽略上一请求的取消结果，避免旧内容覆盖当前游戏。
        }
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
        // 控件内部使用截图实际像素排版，再由 Viewbox 整体适配逻辑像素外框。
        HomeContentPanel.LayoutWidth = profile.NewsWidth;
        HomeContentPanel.LayoutHeight = profile.NewsHeight;
        HomeContentPanel.BannerHeight = profile.NewsHeroHeight;
        LaunchDock.Margin = new Microsoft.UI.Xaml.Thickness(
            0, 0, profile.LaunchRight / rasterizationScale, profile.LaunchBottom / rasterizationScale);
        LaunchButtonRow.Spacing = 12 / rasterizationScale;

        StartGameButton.Width = profile.StartButtonWidth / rasterizationScale;
        StartGameButton.Height = profile.StartButtonHeight / rasterizationScale;
        StartGameButton.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(
            profile.StartButtonHeight / 2 / rasterizationScale);
        var groupRadius = profile.StartButtonHeight / 2 / rasterizationScale;
        StartGameButtonBackground.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(
            groupRadius, 0, 0, groupRadius);
        LaunchButtonGroupBackground.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(groupRadius);
        StartGameButtonContent.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        StartGameButtonContent.RenderTransform = CreateDisplayScaleTransform(rasterizationScale);

        LaunchMenuButton.Width = profile.LaunchMenuButtonWidth / rasterizationScale;
        LaunchMenuButton.Height = profile.StartButtonHeight / rasterizationScale;
        LaunchMenuButton.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(
            profile.StartButtonHeight / 2 / rasterizationScale);
        LaunchMenuIcon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        LaunchMenuIcon.RenderTransform = CreateDisplayScaleTransform(rasterizationScale);
    }

    private void LaunchModeMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 先完成菜单交互结构；后续启动服务接入时只需读取当前选中项。
        SteamLaunchModeItem.IsChecked = ReferenceEquals(sender, SteamLaunchModeItem);
        DirectLaunchModeItem.IsChecked = ReferenceEquals(sender, DirectLaunchModeItem);
        LauncherLaunchModeItem.IsChecked = ReferenceEquals(sender, LauncherLaunchModeItem);
    }

    private async void StartGameButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var game = _currentGame;
        if (game is null)
            return;

        StartGameButton.IsEnabled = false;
        StartGameText.Text = "正在启动";
        try
        {
            // 当前首页仅实现 Steam 模式，由 SteamLaunchService 统一检查 AppID、Steam、ACF 与真实 EXE。
            var result = _steamLaunchService.Launch(_settingsService.Load(), game);
            _logService.AddLog($"[首页启动] {result.Message}");
            if (!result.IsSuccess)
                await ShowInfoAsync(result.Message);
        }
        finally
        {
            StartGameText.Text = "开始游戏";
            StartGameButton.IsEnabled = _currentGame is not null;
        }
    }

    private void LaunchButtonGroup_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        LaunchButtonGroupBackground.Background = CreateBrush(0xFF, 0xFF, 0xD2, 0x1F);
        StartGameButton.Foreground = CreateBrush(0xFF, 0x17, 0x17, 0x17);
        StartGamePlayIcon.Foreground = CreateBrush(0xFF, 0x27, 0x2B, 0x32);
        LaunchMenuIcon.Foreground = CreateBrush(0xFF, 0x27, 0x2B, 0x32);
    }

    private void LaunchButtonGroup_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        LaunchButtonGroupBackground.Background = CreateBrush(0x18, 0x35, 0x39, 0x41);
        StartGameButton.Foreground = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        StartGamePlayIcon.Foreground = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        LaunchMenuIcon.Foreground = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "无法启动游戏",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static ScaleTransform CreateDisplayScaleTransform(double rasterizationScale) => new()
    {
        ScaleX = 1 / rasterizationScale,
        ScaleY = 1 / rasterizationScale
    };

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue) =>
        new(Windows.UI.Color.FromArgb(alpha, red, green, blue));
}
