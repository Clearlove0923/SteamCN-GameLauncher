using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher;

public sealed partial class MainWindow : Window
{
    private string _forceDownloadUrl = "";
    private AppWindow? _appWindow;
    private readonly CustomManifestService _customManifestService = CustomManifestService.Instance;
    private readonly GameExecutableIconService _gameIconService = new();
    private readonly DispatcherTimer _gameLibraryCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _pointerOverGameLibraryTrigger;
    private bool _pointerOverGameLibraryOverlay;
    private string? _pendingHomeGameId;
    private bool _addingCustomManifest;

    private sealed class GameLibraryEntry : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public GameLibraryEntry(string id, string name, string executablePath)
            => (Id, Name, ExecutablePath) = (id, name, executablePath);

        public string Id { get; }
        public string Name { get; }
        public string ExecutablePath { get; }
        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public MainWindow()
    {
        InitializeComponent();
        NavView.CompactPaneLength = LauncherDimensions.SidebarWidth;
        GameLibraryOverlay.Margin = new Thickness(LauncherDimensions.SidebarWidth + 12, 8, 0, 0);
        TitleDragRegion.Margin = new Thickness(LauncherDimensions.SidebarWidth, 0, 150, 0);
        InitializeAppearance();
        _gameLibraryCloseTimer.Tick += (_, _) =>
        {
            _gameLibraryCloseTimer.Stop();
            if (!_pointerOverGameLibraryTrigger && !_pointerOverGameLibraryOverlay)
                GameLibraryOverlay.Visibility = Visibility.Collapsed;
        };

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);

        ConfigureFixedWindow();

        ((FrameworkElement)Content).ActualThemeChanged += (_, _) => ApplyTitleBarColors();

        // 订阅更新通知（主窗口负责全局展示）
        UpdateService.Instance.UpdateAvailable += OnUpdateAvailable;
        _customManifestService.NavigationChanged += OnCustomNavigationChanged;
        Closed += (_, _) =>
        {
            _gameLibraryCloseTimer.Stop();
            _customManifestService.NavigationChanged -= OnCustomNavigationChanged;
        };

        // 首页是默认页面；游戏列表只是切换入口，不拥有独立页面。
        RefreshCustomNavigation();
        NavView.SelectedItem = HomeNavItem;
    }

    // ── 更新通知处理 ──────────────────────────────────────────────────────────

    private void OnUpdateAvailable(string message, string downloadUrl, bool forceUpdate)
    {
        _forceDownloadUrl = downloadUrl;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (forceUpdate)
            {
                // 强制更新：显示全屏遮罩 + 顶部横幅，锁定所有导航
                txtForceUpdateMsg.Text = string.IsNullOrWhiteSpace(message)
                    ? "当前版本存在严重问题，必须更新后才能继续使用。"
                    : message;
                forceUpdateBanner.IsOpen = true;
                forceUpdateOverlay.Visibility = Visibility.Visible;
                NavView.IsEnabled = false;
            }
            else
            {
                // 普通更新：右上角显示提示按钮
                btnUpdateBadge.Visibility = Visibility.Visible;
            }
        });
    }

    // ── 按钮事件 ──────────────────────────────────────────────────────────────

    private void BtnUpdateBadge_Click(object sender, RoutedEventArgs e)
    {
        // 跳转到设置页
        NavView.SelectedItem = NavView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == "Settings");
        ContentFrame.Navigate(typeof(Views.Pages.SettingsPage));
    }

    private async void BtnForceUpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_forceDownloadUrl)) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_forceDownloadUrl));
        }
        catch { }
    }

    // ── 窗口配置 ──────────────────────────────────────────────────────────────

    private void ConfigureFixedWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Title = AppInfo.WindowTitle;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "SteamCN-GameLauncher.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        _appWindow.Resize(new SizeInt32(LauncherDimensions.WindowWidth, LauncherDimensions.WindowHeight));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        if (_appWindow == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = _appWindow.TitleBar;
        var isDark = ((FrameworkElement)Content).ActualTheme == ElementTheme.Dark;

        // 透明背景，让 XAML 内容的颜色透出来
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);

        if (isDark)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 200, 200, 200);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 127, 127, 127);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 255, 255, 255);
        }
        else
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 50, 50, 50);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 127, 127, 127);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 0, 0, 0);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        HideGameLibraryOverlay();

        var tag = item.Tag?.ToString();
        switch (tag)
        {
            case "Home":
                var gameId = _pendingHomeGameId ?? _customManifestService.GetInitialSidebarId();
                _pendingHomeGameId = null;
                if (string.IsNullOrWhiteSpace(gameId)) ShowEmptyGameLibrary();
                else NavigateToHome(gameId);
                break;
            case "SteamConfiguration":
                NavigateToSteamConfiguration();
                break;
            case "Screenshots":
                ContentFrame.Navigate(typeof(Views.Pages.ScreenshotGalleryPage), _customManifestService.GetInitialSidebarId());
                break;
            case "GameLaunchTest":
                ContentFrame.Navigate(typeof(Views.Pages.GameLaunchTestPage));
                break;
            case "Settings":
                ContentFrame.Navigate(typeof(Views.Pages.SettingsPage));
                break;
            case "AppearanceSettings":
                ContentFrame.Navigate(typeof(Views.Pages.AppearanceSettingsPage));
                break;
        }
    }

    private void RefreshCustomNavigation()
    {
        var games = _customManifestService.GetSidebarItems();
        var entries = games.Select(game => new GameLibraryEntry(
            game.Id,
            game.Name,
            game.ClientExePath)).ToList();
        GameLibraryItems.ItemsSource = entries;
        if (entries.Count == 0) HideGameLibraryOverlay();
        _ = LoadGameLibraryIconsAsync(entries);
    }

    private void ShowEmptyGameLibrary()
    {
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage) return;
        ContentFrame.Navigate(typeof(Views.Pages.EmptyGameLibraryPage));
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage page)
            page.AddGameRequested += async (_, _) => await AddGameAsync();
    }

    private void NavigateToCustom(string id)
    {
        if (ContentFrame.Content is Views.Pages.CustomManifestPage current
            && string.Equals(current.PresetId, id, StringComparison.OrdinalIgnoreCase))
        {
            _customManifestService.Select(id);
            return;
        }

        // 先导航，让旧页面在 OnNavigatedFrom 中保存；再记录新选中项，
        // 否则旧页的自动保存会把 CurrentCustomManifestId 改回旧 Id。
        ContentFrame.Navigate(typeof(Views.Pages.CustomManifestPage), id);
        _customManifestService.Select(id);
    }

    private void NavigateToHome(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _customManifestService.Select(id);
        ContentFrame.Navigate(typeof(Views.Pages.LauncherHomePage), id);
    }

    private void NavigateToSteamConfiguration()
    {
        var id = _customManifestService.GetInitialSidebarId();
        if (string.IsNullOrWhiteSpace(id)) ShowEmptyGameLibrary();
        else NavigateToCustom(id);
    }

    private void GameLibraryNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 覆盖层与导航项处于同一个窗口命中树中，不会因 Popup 边界反复触发开关。
        _pointerOverGameLibraryTrigger = true;
        _gameLibraryCloseTimer.Stop();
        RefreshCustomNavigation();
        if (GameLibraryItems.Items.Count > 0)
            GameLibraryOverlay.Visibility = Visibility.Visible;
    }

    private void GameLibraryNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _pointerOverGameLibraryTrigger = false;
        StartGameLibraryCloseTimer();
    }

    private void GameLibraryOverlay_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _pointerOverGameLibraryOverlay = true;
        _gameLibraryCloseTimer.Stop();
    }

    private void GameLibraryOverlay_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _pointerOverGameLibraryOverlay = false;
        StartGameLibraryCloseTimer();
    }

    private void StartGameLibraryCloseTimer()
    {
        _gameLibraryCloseTimer.Stop();
        _gameLibraryCloseTimer.Start();
    }

    private void HideGameLibraryOverlay()
    {
        _gameLibraryCloseTimer.Stop();
        _pointerOverGameLibraryTrigger = false;
        _pointerOverGameLibraryOverlay = false;
        GameLibraryOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task LoadGameLibraryIconsAsync(IEnumerable<GameLibraryEntry> entries)
    {
        foreach (var entry in entries)
            entry.Icon = await _gameIconService.LoadAsync(entry.ExecutablePath, 64);
    }

    private void GameLibraryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameLibraryEntry selected }) return;

        HideGameLibraryOverlay();
        _customManifestService.Select(selected.Id);
        if (ReferenceEquals(NavView.SelectedItem, HomeNavItem))
        {
            NavigateToHome(selected.Id);
            return;
        }

        // 先保存目标游戏，再选中首页；SelectionChanged 只会展示这个明确选择的游戏。
        _pendingHomeGameId = selected.Id;
        NavView.SelectedItem = HomeNavItem;
    }

    private async Task AddGameAsync()
    {
        if (_addingCustomManifest) return;
        _addingCustomManifest = true;
        try
        {
            var textBox = new TextBox { PlaceholderText = "请输入游戏名称", MinWidth = 320 };
            var dialog = new ContentDialog
            {
                Title = "添加游戏",
                Content = textBox,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = NavView.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var name = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await ShowInfoAsync("游戏名称不能为空。");
                return;
            }
            if (_customManifestService.NameExists(name))
            {
                await ShowInfoAsync($"已存在同名游戏「{name}」，请换一个名称。");
                return;
            }

            if (_customManifestService.Create(name) == null)
                await ShowInfoAsync("无法保存新的自定义配置，请稍后重试。");
        }
        finally
        {
            _addingCustomManifest = false;
        }
    }

    private void OnCustomNavigationChanged(string? preferredId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshCustomNavigation();
            // null 表示仅刷新游戏列表顺序，当前页面和内容必须保持不变。
            if (string.IsNullOrWhiteSpace(preferredId)) return;

            var targetId = _customManifestService.GetSidebarItems().Any(game =>
                string.Equals(game.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                ? preferredId
                : null;
            var selectedPage = (NavView.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (selectedPage == "Home")
            {
                if (targetId == null) ShowEmptyGameLibrary();
                else NavigateToHome(targetId);
            }
            else if (selectedPage == "SteamConfiguration")
            {
                if (targetId == null) ShowEmptyGameLibrary();
                else NavigateToCustom(targetId);
            }
        });
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = NavView.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
