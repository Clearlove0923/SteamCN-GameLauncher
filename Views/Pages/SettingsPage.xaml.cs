using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;
using SteamCNGameLauncher.Services.Update;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService = new();
    private readonly SteamService _steamService = new();
    private readonly LogService _logService = LogService.Instance;
    private AppSettings _settings = new();

    // 防止 UI 初始化时触发 Toggled / LostFocus 事件
    private bool _isLoading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // 从 AppInfo 填充版本信息
        txtAppName.Text = AppInfo.AppName;
        txtAppVersion.Text = $"版本 {AppInfo.FullVersion}";
        txtCopyright.Text = AppInfo.Copyright;

        _settings = _settingsService.Load();

        // 加载 Steam 全局配置（v2.2.0 起在此页面统一管理）
        txtSteamPath.Text = _settings.SteamInstallPath;
        txtLibraryPath.Text = _settings.SteamLibraryPath;
        txtSteamId.Text = _settings.SteamId;

        // 若 Steam 路径与库路径为空，尝试自动检测填入
        AutoDetectGlobalPaths();

        tglDeveloperMode.IsOn = _settings.DeveloperMode;
        tglDebugMode.IsOn = _settings.DebugMode;
        tglBetaChannel.IsOn = _settings.BetaChannel;
        SetUpdateSourceSelection(_settings.UpdateSourceId);
        numHomeCacheMegabytes.Value = Math.Clamp(_settings.HomeCacheMaximumMegabytes, 128, 4096);
        numHomeCacheRetentionDays.Value = Math.Clamp(_settings.HomeCacheRetentionDays, 1, 90);

        // 设置语言选项（当前预留，ComboBox 禁用）
        SetLanguageSelection(_settings.Language);

        // 开发者模式相关 UI 状态
        UpdateDeveloperPanel();

        // 填充设置文件路径
        var settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamCN-GameLauncher",
            "settings.json");
        runSettingsPath.Text = settingsPath;

        // 订阅更新通知事件，并回放已缓存的结果（防止检查早于页面加载完成）
        UpdateService.Instance.UpdateAvailable += OnUpdateAvailable;
        UpdateService.Instance.ReplayIfPending();

        _isLoading = false;
        _ = RefreshHomeCacheSizeAsync();
    }

    private async Task RefreshHomeCacheSizeAsync()
    {
        try
        {
            var bytes = await HomeContentServiceFactory.Instance.GetCacheSizeAsync();
            if (!IsLoaded) return;
            txtHomeCacheStatus.Text = bytes == 0
                ? $"当前没有首页缓存。媒体目录：{HomeContentServiceFactory.MediaCacheRoot}"
                : $"当前占用 {FormatBytes(bytes)}；容量上限 {_settings.HomeCacheMaximumMegabytes} MB，最长保留 {_settings.HomeCacheRetentionDays} 天。媒体目录：{HomeContentServiceFactory.MediaCacheRoot}";
        }
        catch (Exception ex)
        {
            if (IsLoaded) txtHomeCacheStatus.Text = $"无法读取缓存大小：{ex.Message}";
        }
    }

    private async void ClearHomeCache_Click(object sender, RoutedEventArgs e)
    {
        btnClearHomeCache.IsEnabled = false;
        txtHomeCacheStatus.Text = "正在清理…";
        try
        {
            await HomeContentServiceFactory.Instance.ClearCacheAsync();
            txtHomeCacheStatus.Text = "缓存已清理；再次打开首页时会重新获取内容。";
        }
        catch (Exception ex)
        {
            txtHomeCacheStatus.Text = $"清理失败：{ex.Message}";
        }
        finally
        {
            btnClearHomeCache.IsEnabled = true;
        }
    }

    private void HomeCachePolicy_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isLoading || double.IsNaN(sender.Value)) return;
        _settings.HomeCacheMaximumMegabytes = (int)Math.Clamp(numHomeCacheMegabytes.Value, 128, 4096);
        _settings.HomeCacheRetentionDays = (int)Math.Clamp(numHomeCacheRetentionDays.Value, 1, 90);
        SaveSettings();
        _ = RefreshHomeCacheSizeAsync();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 取消订阅，防止内存泄漏
        UpdateService.Instance.UpdateAvailable -= OnUpdateAvailable;
    }

    // ── Steam 全局配置：自动检测 ──────────────────────────────────────────────

    private void AutoDetectGlobalPaths()
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(txtSteamPath.Text))
        {
            var steamPath = _steamService.DetectSteamInstallPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                txtSteamPath.Text = steamPath;
                _settings.SteamInstallPath = steamPath;
                _logService.AddLog($"已自动识别 Steam 安装路径：{steamPath}");
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(txtLibraryPath.Text))
        {
            var libraries = _steamService.DetectSteamLibraryPaths();
            if (libraries.Count > 0)
            {
                txtLibraryPath.Text = libraries[0];
                _settings.SteamLibraryPath = libraries[0];
                _logService.AddLog($"已自动识别 SteamLibrary 路径：{libraries[0]}");
                changed = true;
            }
        }

        if (changed) SaveSettings();
    }

    // ── Steam 全局配置：手动编辑 ──────────────────────────────────────────────

    private void SteamPath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var value = txtSteamPath.Text.Trim();
        if (value == _settings.SteamInstallPath) return;
        _settings.SteamInstallPath = value;
        SaveSettings();
    }

    private void LibraryPath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var value = txtLibraryPath.Text.Trim();
        if (value == _settings.SteamLibraryPath) return;
        _settings.SteamLibraryPath = value;
        SaveSettings();
    }

    private void SteamId_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var value = txtSteamId.Text.Trim();
        if (value == _settings.SteamId) return;
        _settings.SteamId = value;
        SaveSettings();
    }

    private void DetectSteamPath_Click(object sender, RoutedEventArgs e)
    {
        var steamPath = _steamService.DetectSteamInstallPath();
        if (steamPath != null)
        {
            txtSteamPath.Text = steamPath;
            _settings.SteamInstallPath = steamPath;
            SaveSettings();
            _logService.AddLog($"已自动识别 Steam 安装路径：{steamPath}");
        }
        else
        {
            _logService.AddLog("未检测到 Steam 安装路径，请手动选择");
        }
    }

    private async void BrowseSteamPath_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        // 验证选择的文件夹中是否有 steam.exe
        var steamExe = System.IO.Path.Combine(folder.Path, "steam.exe");
        if (!System.IO.File.Exists(steamExe))
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = $"所选文件夹中未找到 steam.exe，是否仍然使用该路径？\n{folder.Path}",
                PrimaryButtonText = "使用",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        txtSteamPath.Text = folder.Path;
        _settings.SteamInstallPath = folder.Path;
        SaveSettings();
        _logService.AddLog($"已手动选择 Steam 安装路径：{folder.Path}");
    }

    private async void DetectLibraryPath_Click(object sender, RoutedEventArgs e)
    {
        var libraries = _steamService.DetectSteamLibraryPaths();
        if (libraries.Count == 0)
        {
            _logService.AddLog("未检测到 Steam 游戏库路径，请手动选择");
            await ShowInfoAsync("未检测到 Steam 游戏库路径，请手动选择。");
            return;
        }

        if (libraries.Count == 1)
        {
            txtLibraryPath.Text = libraries[0];
            _settings.SteamLibraryPath = libraries[0];
            SaveSettings();
            _logService.AddLog($"已自动识别 SteamLibrary 路径：{libraries[0]}");
            return;
        }

        var comboBox = new ComboBox
        {
            ItemsSource = libraries,
            SelectedIndex = 0,
            MinWidth = 520
        };

        var dialog = new ContentDialog
        {
            Title = "检测到多个 Steam 游戏库",
            Content = comboBox,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && comboBox.SelectedItem is string selectedPath)
        {
            txtLibraryPath.Text = selectedPath;
            _settings.SteamLibraryPath = selectedPath;
            SaveSettings();
            _logService.AddLog($"已选择 SteamLibrary 路径：{selectedPath}");
        }
    }

    private async void BrowseLibraryPath_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        var steamappsPath = System.IO.Path.Combine(folder.Path, "steamapps");
        if (!System.IO.Directory.Exists(steamappsPath))
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = $"所选文件夹中未找到 steamapps 目录，是否仍然使用该路径？\n{folder.Path}",
                PrimaryButtonText = "使用",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        txtLibraryPath.Text = folder.Path;
        _settings.SteamLibraryPath = folder.Path;
        SaveSettings();
        _logService.AddLog($"已手动选择 SteamLibrary 路径：{folder.Path}");
    }

    // ── 更新通知处理 ──────────────────────────────────────────────────────────

    private void OnUpdateAvailable(LauncherUpdateInfo update)
    {
        // 必须回到 UI 线程更新界面
        DispatcherQueue.TryEnqueue(() =>
        {
            txtUpdateMessage.Text = $"版本 {update.Version}\n\n{update.ReleaseNotes}";
            updateCard.Visibility = Visibility.Visible;
        });
    }

    // ── 手动检查更新 ──────────────────────────────────────────────────────────

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await ShowUpdateSourceChoiceDialogAsync();
    }

    private async Task ShowUpdateSourceChoiceDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = $"检查更新  {AppInfo.FullVersion}",
            PrimaryButtonText = "手动下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.None,
            XamlRoot = XamlRoot
        };

        var sourcePanel = new StackPanel
        {
            Spacing = 10,
            MinWidth = 520,
            MaxWidth = 620
        };
        sourcePanel.Children.Add(new TextBlock
        {
            Text = "选择一个发布源检查新版本。更新只会在你确认后下载和安装。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        sourcePanel.Children.Add(CreateUpdateSourceOption(
            dialog,
            UpdateSourceIds.Cnb,
            "CNB 更新服务",
            "国内推荐，连接速度通常更稳定",
            useAccentButton: true));
        sourcePanel.Children.Add(CreateUpdateSourceOption(
            dialog,
            UpdateSourceIds.GitHub,
            "GitHub 更新服务",
            "备用来源；访问受限时建议改用 CNB",
            useAccentButton: false));

        sourcePanel.Children.Add(new TextBlock
        {
            Text = $"手动下载将打开当前首选来源：{UpdateSourcePolicy.GetDisplayName(GetSelectedUpdateSource())}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        });
        dialog.Content = sourcePanel;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await OpenManualUpdatePageAsync(GetSelectedUpdateSource());
    }

    private Border CreateUpdateSourceOption(
        ContentDialog dialog,
        string sourceId,
        string title,
        string description,
        bool useAccentButton)
    {
        var actionButton = new Button
        {
            Content = "立即检查",
            MinWidth = 112,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (useAccentButton)
        {
            actionButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0, 120, 212));
            actionButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }

        actionButton.Click += async (_, _) =>
        {
            actionButton.IsEnabled = false;
            dialog.Hide();
            await Task.Yield();
            await SelectSourceAndStartUpdaterAsync(sourceId);
        };

        var content = new Grid { ColumnSpacing = 16 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72,
                    FontSize = 12
                }
            }
        });
        Grid.SetColumn(actionButton, 1);
        content.Children.Add(actionButton);

        return new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppearanceCardBackground"],
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(64, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = content
        };
    }

    private async Task SelectSourceAndStartUpdaterAsync(string sourceId)
    {
        sourceId = UpdateSourcePolicy.Normalize(sourceId);
        SetUpdateSourceSelection(sourceId);
        _settings.UpdateSourceId = sourceId;
        SaveSettings();
        txtCheckUpdateStatus.Text = $"已选择 {UpdateSourcePolicy.GetDisplayName(sourceId)} 检查更新。";

        await StartUpdaterOrOfferManualDownloadAsync(sourceId);
    }

    private async Task OpenManualUpdatePageAsync(string sourceId)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri(UpdateSourcePolicy.GetManualDownloadUrl(sourceId)));
        }
        catch (Exception ex)
        {
            txtCheckUpdateStatus.Text = "无法打开发布页，请稍后重试。";
            _logService.AddLog($"[更新] 无法打开 {UpdateSourcePolicy.GetDisplayName(sourceId)} 发布页：{ex.Message}");
        }
    }

    // ── 立即下载 ──────────────────────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        await StartUpdaterOrOfferManualDownloadAsync(GetSelectedUpdateSource());
    }

    private async Task StartUpdaterOrOfferManualDownloadAsync(string sourceId)
    {
        if (KachinaUpdateService.Instance.TryStart(sourceId, out var error))
        {
            (App.MainWindow as MainWindow)?.ExitForUpdate();
            return;
        }

        _logService.AddLog($"[更新] 将回退到手动下载：{error}");
        var manualUrl = UpdateSourcePolicy.GetManualDownloadUrl(sourceId);
        var dialog = new ContentDialog
        {
            Title = "无法启动自动更新",
            Content = $"{error}\n\n可以改用浏览器打开 {UpdateSourcePolicy.GetDisplayName(sourceId)} 发布页手动下载。",
            PrimaryButtonText = "打开下载页",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(manualUrl));
            }
            catch (Exception ex)
            {
                _logService.AddLog($"[更新] 无法打开手动下载页面：{ex.Message}");
            }
        }
    }

    private string GetSelectedUpdateSource()
    {
        if (cmbUpdateSource.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return UpdateSourcePolicy.Normalize(tag);
        return UpdateSourcePolicy.Normalize(_settings.UpdateSourceId);
    }

    private void UpdateSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || cmbUpdateSource.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        SetUpdateSourceSelection(tag);
        _settings.UpdateSourceId = UpdateSourcePolicy.Normalize(tag);
        SaveSettings();
    }

    private void PreferredUpdateSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || cmbPreferredUpdateSource.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        SetUpdateSourceSelection(tag);
        _settings.UpdateSourceId = UpdateSourcePolicy.Normalize(tag);
        SaveSettings();
    }

    // ── 开发者模式 ────────────────────────────────────────────────────────────

    private void DeveloperMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _settings.DeveloperMode = tglDeveloperMode.IsOn;
        UpdateDeveloperPanel();
        SaveSettings();
    }

    private void DebugMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _settings.DebugMode = tglDebugMode.IsOn;
        SaveSettings();
    }

    private void BetaChannel_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _settings.BetaChannel = tglBetaChannel.IsOn;
        SaveSettings();
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (cmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _settings.Language = tag;
            SaveSettings();
        }
    }

    private async void OpenSettingsDir_Click(object sender, RoutedEventArgs e)
    {
        var settingsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamCN-GameLauncher");

        try
        {
            if (!System.IO.Directory.Exists(settingsDir))
                System.IO.Directory.CreateDirectory(settingsDir);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = settingsDir,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "错误",
                Content = $"无法打开目录：{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private void UpdateDeveloperPanel()
    {
        developerOptionsPanel.Visibility = tglDeveloperMode.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetLanguageSelection(string language)
    {
        foreach (var item in cmbLanguage.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && tag == language)
            {
                cmbLanguage.SelectedItem = item;
                return;
            }
        }
        // 默认选中简体中文
        if (cmbLanguage.Items.Count > 0)
            cmbLanguage.SelectedIndex = 0;
    }

    private void SetUpdateSourceSelection(string? sourceId)
    {
        var normalized = UpdateSourcePolicy.Normalize(sourceId);
        SelectComboTag(cmbUpdateSource, normalized);
        SelectComboTag(cmbPreferredUpdateSource, normalized);
    }

    private static void SelectComboTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemTag && string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SaveSettings()
    {
        // 设置页只写入自己管理的全局字段，避免用过期快照覆盖动态自定义配置。
        _settingsService.Update(settings =>
        {
            settings.SteamInstallPath = _settings.SteamInstallPath;
            settings.SteamLibraryPath = _settings.SteamLibraryPath;
            settings.SteamId = _settings.SteamId;
            settings.DeveloperMode = _settings.DeveloperMode;
            settings.DebugMode = _settings.DebugMode;
            settings.BetaChannel = _settings.BetaChannel;
            settings.UpdateSourceId = UpdateSourcePolicy.Normalize(_settings.UpdateSourceId);
            settings.Language = _settings.Language;
            settings.HomeCacheMaximumMegabytes = _settings.HomeCacheMaximumMegabytes;
            settings.HomeCacheRetentionDays = _settings.HomeCacheRetentionDays;
        });
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
