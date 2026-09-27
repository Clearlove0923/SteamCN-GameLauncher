using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class LauncherHomePage : Page
{
    private readonly HomeLayoutProfileCatalog _layoutProfiles = new();
    private readonly SettingsService _settingsService = new();
    private readonly SteamLaunchService _steamLaunchService = new();
    private readonly DirectGameLaunchService _directGameLaunchService = new();
    private readonly AppearanceService _appearanceService = AppearanceService.Instance;
    private readonly LogService _logService = LogService.Instance;
    private readonly HomeBackdropCoordinator _homeBackdrop = HomeBackdropCoordinator.Instance;
    private readonly HomeVideoVariantSelector _videoVariantSelector = HomeContentServiceFactory.VideoSelector;
    private readonly Guid _homeBackdropOwner = Guid.NewGuid();
    private readonly IHomeContentService _homeContentService;
    private string? _activeLayoutProfileId;
    private CancellationTokenSource? _homeContentCancellation;
    private Models.CustomManifestPreset? _currentGame;
    private HomeContent? _currentHomeContent;
    private bool _forceBlackBackground;
    private HomeContentRequest? _activeHomeRequest;
    private SupportedGameRegistry.Match? _activeHomeMatch;
    private bool _navigatedAway;

    public LauncherHomePage()
    {
        InitializeComponent();
        Loaded += LauncherHomePage_Loaded;
        Unloaded += LauncherHomePage_Unloaded;
        // 窗口跨显示器或缩放率变化时，保持资讯栏的截图实际像素尺寸不变。
        SizeChanged += (_, _) => ApplyLayoutProfile(_activeLayoutProfileId);

        _homeContentService = HomeContentServiceFactory.Instance.GetOrCreate(_settingsService.Load());
    }

    private void LauncherHomePage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_navigatedAway) return;
        _appearanceService.Changed -= ApplyHomeAppearance;
        _appearanceService.Changed += ApplyHomeAppearance;
        if (_homeContentService is IHomeContentRefreshSource refreshSource)
        {
            // 磁盘旧缓存可以立即显示；后台下载完成后由同一服务推送最新数据。
            refreshSource.ContentRefreshed -= HomeContentService_ContentRefreshed;
            refreshSource.ContentRefreshed += HomeContentService_ContentRefreshed;
        }
        ApplyLayoutProfile(_activeLayoutProfileId);
        ApplyHomeAppearance();
    }

    private void LauncherHomePage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _appearanceService.Changed -= ApplyHomeAppearance;
        if (_homeContentService is IHomeContentRefreshSource refreshSource)
            refreshSource.ContentRefreshed -= HomeContentService_ContentRefreshed;
        _homeBackdrop.Clear(_homeBackdropOwner);
    }

    private void ApplyHomeAppearance()
    {
        // 页面只控制内容与播放意图，真正的视频播放器始终归窗口底层所有。
        if (_navigatedAway) return;
        var profile = _appearanceService.Settings.GetEffective(AppearancePageIds.Home);
        NewsPanel.Visibility = profile.ShowHomeNews && _activeHomeMatch is not null
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ShowHomeAnimationItem.IsChecked = profile.ShowHomeAnimation;
        ShowHomeNewsItem.IsChecked = profile.ShowHomeNews;
        _homeBackdrop.Show(_homeBackdropOwner, _currentHomeContent?.Background,
            profile.ShowHomeAnimation, _forceBlackBackground);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navigatedAway = false;
        var id = e.Parameter as string ?? CustomManifestService.Instance.GetInitialSidebarId();
        if (!string.IsNullOrWhiteSpace(id) && CustomManifestService.Instance.GetById(id) is { } game)
            _ = ShowGameAsync(game);
        else
        {
            _currentGame = null;
            _activeHomeRequest = null;
            _activeHomeMatch = null;
            StartGameButton.IsEnabled = false;
            // 还没选游戏时隐藏轮播 + 资讯 UI：
            //  - 避免显示空 "资讯" tab 干扰首次启动用户；
            //  - 避免 HomeContentPanel 残留上一次的游戏数据；
            //  - 切换到真游戏后由 ApplyHomeAppearance 按外观档案恢复可见性。
            _currentHomeContent = null;
            _forceBlackBackground = true;
            HomeContentPanel.SetContent(null);
            NewsPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ApplyHomeAppearance();
        }
    }

    public void SwitchGame(string id)
    {
        // 复用现有首页实例，避免 Frame 导航卸载背景时出现短暂黑屏。
        if (CustomManifestService.Instance.GetById(id) is { } game)
            _ = ShowGameAsync(game);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigatedAway = true;
        _activeHomeRequest = null;
        _homeContentCancellation?.Cancel();
        _homeBackdrop.Clear(_homeBackdropOwner);
        base.OnNavigatedFrom(e);
    }

    private async Task ShowGameAsync(Models.CustomManifestPreset game)
    {
        _currentGame = game;
        _activeHomeMatch = null;
        _forceBlackBackground = false;
        // 新游戏元数据与解码器准备期间保留旧背景，资讯立即清空，避免旧内容串到新游戏。
        HomeContentPanel.SetContent(null);
        NewsPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ApplyLaunchMode(game.HomeLaunchModeId);
        StartGameButton.IsEnabled = true;
        ApplyLayoutProfile(game.HomeLayoutProfileId);

        // 匹配用户填写的真实游戏 EXE；无法识别时再按路径中的官方游戏目录匹配。
        // Steam AppID 和预设显示名都不参与首页来源选择。
        _homeContentCancellation?.Cancel();
        _activeHomeRequest = null;
        _activeHomeMatch = null;
        if (!SupportedGameRegistry.TryMatch(game.ClientExePath, game.InstallDir, out var match))
        {
            _currentHomeContent = null;
            HomeContentPanel.SetContent(null);
            ResetBackgroundToDefaultBlack();
            return;
        }
        _activeHomeMatch = match;

        _homeContentCancellation = new CancellationTokenSource();
        try
        {
            var request = new HomeContentRequest
            {
                GameId = match.GameId,
                ExecutablePath = game.ClientExePath,
                InstallDirectory = game.InstallDir,
                CacheFolderName = SupportedGameRegistry.GetCacheFolderName(match, game.ClientExePath, game.InstallDir),
                ProviderId = "auto",
                Locale = "zh-CN",
                // Python 使用同一映射表独立选择 Provider；此处选项用于缓存版本区分。
                ProviderOptions = match.ProviderOptions
            };
            _activeHomeRequest = request;
            var result = await _homeContentService.GetAsync(request, _homeContentCancellation.Token);
            if (_navigatedAway || !ReferenceEquals(_activeHomeRequest, request)) return;
            _currentHomeContent = _videoVariantSelector.Select(request, result.Content);
            HomeContentPanel.SetContent(_currentHomeContent, match.NewsCategoryLabels);
            ApplyHomeAppearance();
            if (result.IsStale)
                _logService.AddLog("[首页缓存] 已立即显示本地缓存，正在后台刷新最新内容");
        }
        catch (OperationCanceledException)
        {
            // 快速切换游戏时忽略上一请求的取消结果，避免旧内容覆盖当前游戏。
        }
        catch (Exception ex)
        {
            if (_navigatedAway || _currentGame != game) return;
            _logService.AddLog($"[首页内容] 切换游戏失败：{ex.Message}");
            ResetBackgroundToDefaultBlack();
        }
    }

    private void ResetBackgroundToDefaultBlack()
    {
        _forceBlackBackground = true;
        _homeBackdrop.Show(_homeBackdropOwner, null, false, forceBlack: true);
        // 未匹配安装文件时也把资讯 + 轮播藏起来，避免上一个游戏的真实数据
        // 残留在未验证游戏的窗口上；切回适配游戏时由 ApplyHomeAppearance 恢复。
        _currentHomeContent = null;
        HomeContentPanel.SetContent(null);
        NewsPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void HomeContentService_ContentRefreshed(object? sender, HomeContentRefreshedEventArgs e)
    {
        // Worker 下载可晚于下一次切换；请求哈希与引用都匹配才允许刷新当前页面。
        var active = _activeHomeRequest;
        if (active is null || HomeCacheKey.Create(active) != HomeCacheKey.Create(e.Request))
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_navigatedAway || _activeHomeRequest != active || !IsLoaded) return;
            _currentHomeContent = _videoVariantSelector.Select(active, e.Result.Content);
            HomeContentPanel.SetContent(_currentHomeContent, _activeHomeMatch?.NewsCategoryLabels);
            ApplyHomeAppearance();
        });
    }

    private void HomeDisplayMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var profile = _appearanceService.Settings.GetEffective(AppearancePageIds.Home);
        profile.ShowHomeAnimation = ShowHomeAnimationItem.IsChecked;
        profile.ShowHomeNews = ShowHomeNewsItem.IsChecked;
        _appearanceService.Preview();
        if (!_appearanceService.Save())
            _logService.AddLog("[首页显示] 设置保存失败，本次运行仍使用当前选择");
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
        var mode = ReferenceEquals(sender, DirectCnLaunchModeItem)
            ? HomeLaunchModeIds.DirectCn
            : ReferenceEquals(sender, SteamInternationalLaunchModeItem)
                ? HomeLaunchModeIds.SteamInternational
                : HomeLaunchModeIds.SteamCn;
        ApplyLaunchMode(mode);

        if (_currentGame is not { } game)
            return;

        var updated = game.Clone();
        updated.HomeLaunchModeId = mode;
        if (CustomManifestService.Instance.Update(updated))
        {
            _currentGame = updated;
            _logService.AddLog($"[首页启动] 已保存启动方式：{GetLaunchModeName(mode)}");
        }
        else
        {
            _logService.AddLog("[首页启动] 启动方式保存失败，本次页面内仍使用当前选择");
            game.HomeLaunchModeId = mode;
        }
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
            var result = game.HomeLaunchModeId switch
            {
                HomeLaunchModeIds.DirectCn => _directGameLaunchService.Launch(game),
                HomeLaunchModeIds.SteamInternational =>
                    _steamLaunchService.LaunchInternational(_settingsService.Load(), game),
                _ => _steamLaunchService.Launch(_settingsService.Load(), game),
            };
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

    private void ApplyLaunchMode(string? mode)
    {
        var normalized = HomeLaunchModeIds.IsSupported(mode) ? mode! : HomeLaunchModeIds.SteamCn;
        SteamCnLaunchModeItem.IsChecked = normalized == HomeLaunchModeIds.SteamCn;
        DirectCnLaunchModeItem.IsChecked = normalized == HomeLaunchModeIds.DirectCn;
        SteamInternationalLaunchModeItem.IsChecked = normalized == HomeLaunchModeIds.SteamInternational;
    }

    private static string GetLaunchModeName(string mode) => mode switch
    {
        HomeLaunchModeIds.DirectCn => "国服",
        HomeLaunchModeIds.SteamInternational => "Steam 玩国际服",
        _ => "Steam 玩国服",
    };

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
