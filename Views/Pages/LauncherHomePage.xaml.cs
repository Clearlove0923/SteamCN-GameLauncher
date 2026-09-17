using System.Net.Http;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
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
    private readonly MediaPlayer _backgroundPlayer = new() { IsLoopingEnabled = true, AutoPlay = true };
    private readonly IHomeContentService _homeContentService;
    private string? _activeLayoutProfileId;
    private CancellationTokenSource? _homeContentCancellation;
    private Models.CustomManifestPreset? _currentGame;
    private HomeContent? _currentHomeContent;
    private string? _activeVideoSource;

    public LauncherHomePage()
    {
        InitializeComponent();
        HomeBackgroundVideo.SetMediaPlayer(_backgroundPlayer);
        _backgroundPlayer.MediaFailed += BackgroundPlayer_MediaFailed;
        Loaded += LauncherHomePage_Loaded;
        Unloaded += LauncherHomePage_Unloaded;
        // 窗口跨显示器或缩放率变化时，保持资讯栏的截图实际像素尺寸不变。
        SizeChanged += (_, _) => ApplyLayoutProfile(_activeLayoutProfileId);

        var settings = _settingsService.Load();
        _homeContentService = CreateHomeContentService(settings);
    }

    /// <summary>
    /// Pick the home-content service to use for this session.
    ///
    /// When <see cref="AppSettings.HomeContentWorkerBaseUrl"/> is set and not the
    /// built-in default placeholder, talk to the Python Worker over HTTP via
    /// <see cref="FastApiHomeContentService"/>. Otherwise fall back to
    /// <see cref="PreviewHomeContentService"/> so the page renders something
    /// even before the worker is launched.
    /// </summary>
    private static IHomeContentService CreateHomeContentService(AppSettings settings)
    {
        var baseUrl = settings.HomeContentWorkerBaseUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl))
        {
            return new PreviewHomeContentService();
        }

        try
        {
            var endpoint = new Uri(new Uri(baseUrl), "/v1/home-content");
            var transport = new HttpHomeContentTransport(
                new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.HomeContentWorkerTimeoutSeconds)) },
                endpoint);
            return new FastApiHomeContentService(transport);
        }
        catch (UriFormatException)
        {
            return new PreviewHomeContentService();
        }
    }

    private void LauncherHomePage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _appearanceService.Changed -= ApplyHomeAppearance;
        _appearanceService.Changed += ApplyHomeAppearance;
        ApplyLayoutProfile(_activeLayoutProfileId);
        ApplyHomeAppearance();
    }

    private void LauncherHomePage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _appearanceService.Changed -= ApplyHomeAppearance;
        StopHomeAnimation();
    }

    private void ApplyHomeAppearance()
    {
        var profile = _appearanceService.Settings.GetEffective(AppearancePageIds.Home);
        NewsPanel.Visibility = profile.ShowHomeNews ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ShowHomeAnimationItem.IsChecked = profile.ShowHomeAnimation;
        ShowHomeNewsItem.IsChecked = profile.ShowHomeNews;
        ApplyHomeAnimation(profile.ShowHomeAnimation ? _currentHomeContent?.Background : null);
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
        ApplyLaunchMode(game.HomeLaunchModeId);
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
                ProviderId = ResolveProviderId(game),
                Locale = "zh-CN"
            }, _homeContentCancellation.Token);
            _currentHomeContent = result.Content;
            HomeContentPanel.SetContent(result.Content);
            ApplyHomeAppearance();
        }
        catch (OperationCanceledException)
        {
            // 快速切换游戏时忽略上一请求的取消结果，避免旧内容覆盖当前游戏。
        }
    }

    /// <summary>
    /// Pick the ProviderId for the request. Custom manifest may declare a
    /// stable Python Provider ID (e.g. "kuro-launcher", "hoyoplay-json");
    /// when empty we fall back to the preview service so the page still
    /// renders something before the worker is wired up.
    /// </summary>
    private static string ResolveProviderId(Models.CustomManifestPreset game)
    {
        var declared = game.HomeContentProviderId?.Trim();
        return string.IsNullOrEmpty(declared)
            ? PreviewHomeContentService.ProviderId
            : declared!;
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

    private void ApplyHomeAnimation(HomeBackground? background)
    {
        var source = ResolveVideoSource(background?.VideoUrl);
        if (source is null)
        {
            ShowStaticBackground();
            return;
        }

        var key = source.AbsoluteUri;
        if (_activeVideoSource == key)
        {
            HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            HomeBackgroundImage.Source = null;
            HomeBackgroundVideo.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            _backgroundPlayer.Play();
            return;
        }

        try
        {
            _backgroundPlayer.Source = MediaSource.CreateFromUri(source);
            _activeVideoSource = key;
            HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            HomeBackgroundImage.Source = null;
            HomeBackgroundVideo.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch (Exception ex)
        {
            StopHomeAnimation();
            _logService.AddLog($"[首页背景] 动画无法播放，已回退到背景图片：{ex.Message}");
        }
    }

    private void ShowStaticBackground()
    {
        var options = _appearanceService.CurrentProfile.Current;
        if (string.IsNullOrWhiteSpace(options.SourceImage))
        {
            HomeBackgroundImage.Source = null;
            HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        try
        {
            var path = _appearanceService.GetImagePath(options.SourceImage);
            HomeBackgroundImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
            HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch (Exception ex)
        {
            HomeBackgroundImage.Source = null;
            HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            _logService.AddLog($"[首页背景] 静态背景图加载失败：{ex.Message}");
        }
    }

    private static Uri? ResolveVideoSource(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return null;
        if (uri.IsFile)
            return File.Exists(uri.LocalPath) ? uri : null;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? uri : null;
    }

    private void BackgroundPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StopHomeAnimation();
            _logService.AddLog($"[首页背景] 动画播放失败，已回退到背景图片：{args.ErrorMessage}");
        });
    }

    private void StopHomeAnimation()
    {
        _backgroundPlayer.Pause();
        _backgroundPlayer.Source = null;
        _activeVideoSource = null;
        HomeBackgroundVideo.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        HomeBackgroundImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        HomeBackgroundImage.Source = null;
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
