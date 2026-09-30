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
    private AppWindow? _appWindow;
    private readonly CustomManifestService _customManifestService = CustomManifestService.Instance;
    private readonly GameExecutableIconService _gameIconService = new();
    private readonly DispatcherTimer _gameLibraryCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _pointerOverGameLibraryTrigger;
    private bool _pointerOverGameLibraryOverlay;
    private string? _pendingHomeGameId;
    private bool _addingCustomManifest;
    private bool _restoringNavigationSelection;
    private bool _confirmingNavigation;
    private bool _updatePromptShown;
    private NavigationViewItem? _lastAcceptedNavigationItem;

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
        PlayTimeService.Instance.Start();
        LogService.Instance.AttachDispatcher(
            () => DispatcherQueue.HasThreadAccess,
            update => DispatcherQueue.TryEnqueue(() => update()));
        NavView.CompactPaneLength = LauncherDimensions.SidebarWidth;
        SidebarBackdrop.Width = LauncherDimensions.SidebarWidth;
        // 与侧栏重叠一个逻辑像素，鼠标横向移动时不会经过无命中间隙。
        GameLibraryOverlay.Margin = new Thickness(LauncherDimensions.SidebarWidth - 1, 12, 0, 0);
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
            PlayTimeService.Instance.Dispose();
            _gameLibraryCloseTimer.Stop();
            _customManifestService.NavigationChanged -= OnCustomNavigationChanged;
            UpdateService.Instance.UpdateAvailable -= OnUpdateAvailable;
        };

        // 首页是默认页面；游戏列表只是切换入口，不拥有独立页面。
        RefreshCustomNavigation();
        _lastAcceptedNavigationItem = HomeNavItem;
        NavView.SelectedItem = HomeNavItem;
        // NavigationView 可能先选中首页却不触发 SelectionChanged；下一轮 UI 消息中补导航。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ContentFrame.Content is null)
                NavigateToHome(_customManifestService.GetInitialSidebarId());
        });
    }

    // ── 更新通知处理 ──────────────────────────────────────────────────────────

    private void OnUpdateAvailable(LauncherUpdateInfo update)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            btnUpdateBadge.Visibility = Visibility.Visible;
            if (_updatePromptShown) return;
            _updatePromptShown = true;
            // 与手动检查共用 Kachina 窗口；此时不退出应用，也不自动下载安装。
            var sourceId = update.SourceId ?? new SettingsService().Load().UpdateSourceId;
            var error = await Services.Update.KachinaUpdateService.Instance.ShowAsync(sourceId, update);
            if (error is not null)
            {
                _updatePromptShown = false;
                LogService.Instance.AddLog($"[更新] 无法打开更新窗口：{error}");
                // 启动期不叠加错误弹窗，保留设置页角标供用户重试。
            }
        });
    }

    // ── 按钮事件 ──────────────────────────────────────────────────────────────

    private void BtnUpdateBadge_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings();
    }

    private void NavigateToSettings()
    {
        // 统一通过 SelectionChanged 导航，使未保存配置检查不会被绕过。
        NavView.SelectedItem = NavView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == "Settings");
    }

    // ── 窗口配置 ──────────────────────────────────────────────────────────────

    private void ConfigureFixedWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        InitializeTrayIcon();
        _appWindow.Closing += (_, args) =>
        {
            // 标题栏关闭只隐藏窗口；托盘“退出”设置标志后才真正释放播放器与 Worker。
            if (!_exitFromTray && _trayIcon is not null)
            {
                args.Cancel = true;
                SuspendHomeMediaForTray();
                _appWindow.Hide();
                LogService.Instance.AddLog("[窗口] 已隐藏到系统托盘");
                return;
            }
            LogService.Instance.AddLog("[窗口] 收到关闭请求，正在释放首页媒体");
            PrepareHomeMediaForClose();
        };

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

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        if (_restoringNavigationSelection) return;

        // 配置页允许用户保存、放弃或取消本次侧边栏切换。
        if (!_confirmingNavigation
            && !ReferenceEquals(item, _lastAcceptedNavigationItem)
            && ContentFrame.Content is Views.Pages.CustomManifestPage configurationPage)
        {
            _confirmingNavigation = true;
            var canLeave = false;
            try
            {
                canLeave = await configurationPage.ConfirmUnsavedChangesAsync();
            }
            finally
            {
                _confirmingNavigation = false;
            }

            if (!canLeave)
            {
                _pendingHomeGameId = null;
                _restoringNavigationSelection = true;
                try
                {
                    NavView.SelectedItem = _lastAcceptedNavigationItem;
                }
                finally
                {
                    _restoringNavigationSelection = false;
                }
                return;
            }
        }

        _lastAcceptedNavigationItem = item;
        HideGameLibraryOverlay();

        var tag = item.Tag?.ToString();
        switch (tag)
        {
            case "Home":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.Home);
                var gameId = _pendingHomeGameId ?? _customManifestService.GetInitialSidebarId();
                _pendingHomeGameId = null;
                if (string.IsNullOrWhiteSpace(gameId)) ShowEmptyGameLibrary();
                else NavigateToHome(gameId);
                break;
            case "SteamConfiguration":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.GameConfiguration);
                NavigateToSteamConfiguration();
                break;
            case "Screenshots":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.Screenshots);
                ContentFrame.Navigate(typeof(Views.Pages.ScreenshotGalleryPage), _customManifestService.GetInitialSidebarId());
                break;
            case "GameLaunchTest":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.GameConfiguration);
                ContentFrame.Navigate(typeof(Views.Pages.GameLaunchTestPage));
                break;
            case "Settings":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.Settings);
                ContentFrame.Navigate(typeof(Views.Pages.SettingsPage));
                break;
            case "AppearanceSettings":
                AppearanceService.Instance.SetActivePage(AppearancePageIds.Appearance);
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
        _ = LoadGameLibraryIconsAsync(entries);
    }

    private void ShowEmptyGameLibrary()
    {
        AppearanceService.Instance.SetActivePage(AppearancePageIds.Home);
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage) return;
        ContentFrame.Navigate(typeof(Views.Pages.EmptyGameLibraryPage));
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage page)
            page.AddGameRequested += async (_, _) => await AddGameAsync();
    }

    private void NavigateToCustom(string id)
    {
        AppearanceService.Instance.SetActivePage(AppearancePageIds.GameConfiguration);
        if (ContentFrame.Content is Views.Pages.CustomManifestPage current
            && string.Equals(current.PresetId, id, StringComparison.OrdinalIgnoreCase))
        {
            _customManifestService.Select(id);
            return;
        }

        // 页面内部负责提示未保存内容；这里仅执行已确认的配置导航。
        ContentFrame.Navigate(typeof(Views.Pages.CustomManifestPage), id);
        _customManifestService.Select(id);
    }

    private void NavigateToHome(string? id)
    {
        AppearanceService.Instance.SetActivePage(AppearancePageIds.Home);
        if (!string.IsNullOrWhiteSpace(id)) _customManifestService.Select(id);
        if (!string.IsNullOrWhiteSpace(id) && ContentFrame.Content is Views.Pages.LauncherHomePage current)
        {
            // 切游戏不重新导航首页，窗口动画继续播放直到下一段视频就绪。
            current.SwitchGame(id);
            return;
        }
        ContentFrame.Navigate(typeof(Views.Pages.LauncherHomePage), id);
    }

    private void NavigateToSteamConfiguration()
    {
        // 没有已添加游戏时仍打开内置配置，用户可在页面中点击“新建”。
        var id = _customManifestService.GetInitialSidebarId() ?? _customManifestService.GetBuiltInId();
        NavigateToCustom(id);
    }

    private void GameLibraryNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 覆盖层与导航项处于同一个窗口命中树中，不会因 Popup 边界反复触发开关。
        _pointerOverGameLibraryTrigger = true;
        _gameLibraryCloseTimer.Stop();
        // 列表只在配置变化时重建；悬停期间替换 ItemsSource 会改变命中树并造成闪烁。
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
        if (ReferenceEquals(NavView.SelectedItem, HomeNavItem))
        {
            NavigateToHome(selected.Id);
            return;
        }

        // 先暂存目标游戏，未保存提示确认离开后才真正切换当前配置。
        _pendingHomeGameId = selected.Id;
        NavView.SelectedItem = HomeNavItem;
    }

    private void OpenGameConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        HideGameLibraryOverlay();
        var configurationItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(), "SteamConfiguration", StringComparison.Ordinal));
        if (configurationItem == null) return;

        if (ReferenceEquals(NavView.SelectedItem, configurationItem))
            NavigateToSteamConfiguration();
        else
            NavView.SelectedItem = configurationItem;
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
