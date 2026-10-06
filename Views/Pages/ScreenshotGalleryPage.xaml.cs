using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using Windows.Storage.Pickers;
using Windows.UI;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class ScreenshotGalleryPage : Page
{
    private readonly CustomManifestService _manifestService = CustomManifestService.Instance;
    private readonly ScreenshotCatalogService _catalogService = new();
    private readonly ObservableCollection<ScreenshotDateGroup> _groups = [];
    private readonly CollectionViewSource _groupedScreenshots = new() { IsSourceGrouped = true };
    private readonly Dictionary<DateTime, int> _screenshotCountsByDate = [];
    private IReadOnlyList<CustomManifestPreset> _games = [];
    private CancellationTokenSource? _loadCancellation;
    private bool _loadingSelectors = true;
    private bool _loadingCalendar;

    public ScreenshotGalleryPage()
    {
        InitializeComponent();
        _groupedScreenshots.Source = _groups;
        ScreenshotGrid.ItemsSource = _groupedScreenshots.View;
        Unloaded += (_, _) => CancelLoading();
        _loadingSelectors = false;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loadingSelectors = true;
        _games = _manifestService.GetSidebarItems();
        GameSelector.ItemsSource = _games;
        var index = e.Parameter is string id
            ? _games.ToList().FindIndex(game => string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase))
            : -1;
        GameSelector.SelectedIndex = index >= 0 ? index : _games.Count > 0 ? 0 : -1;
        GameSelector.IsEnabled = _games.Count > 0;
        SortSelector.IsEnabled = _games.Count > 0;
        ChooseFolderButton.IsEnabled = _games.Count > 0;
        DateJumpPicker.IsEnabled = false;
        _loadingSelectors = false;
        _ = LoadSelectedGameAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelLoading();
        base.OnNavigatedFrom(e);
    }

    private async void GameSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSelectors) await LoadSelectedGameAsync();
    }

    private async void SortSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSelectors && GameSelector != null) await LoadSelectedGameAsync();
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameSelector.SelectedItem is not CustomManifestPreset selected) return;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            selected.ScreenshotDirectoryPath = folder.Path;
            if (!_manifestService.Update(selected))
            {
                ShowEmptyState("无法保存截图路径", "请检查设置目录是否可写后重试。");
                return;
            }

            _games = _manifestService.GetSidebarItems();
            var refreshed = _games.FirstOrDefault(game =>
                string.Equals(game.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null)
            {
                _loadingSelectors = true;
                GameSelector.ItemsSource = _games;
                GameSelector.SelectedItem = refreshed;
                _loadingSelectors = false;
            }
            await LoadSelectedGameAsync();
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[截图] 选择文件夹失败：{ex.Message}");
            ShowEmptyState("无法选择截图文件夹", "请确认目录仍可访问后重试。");
        }
    }

    private async Task LoadSelectedGameAsync()
    {
        CancelLoading();
        _groups.Clear();
        _screenshotCountsByDate.Clear();
        _loadingCalendar = true;
        DateJumpPicker.Date = null;
        DateJumpPicker.IsEnabled = false;
        _loadingCalendar = false;
        ScreenshotGrid.Visibility = Visibility.Collapsed;

        if (GameSelector.SelectedItem is not CustomManifestPreset game)
        {
            ShowEmptyState("尚未添加游戏", "请先在游戏配置中添加游戏。");
            return;
        }

        ChooseFolderButton.Content = string.IsNullOrWhiteSpace(game.ScreenshotDirectoryPath)
            ? "选择文件夹"
            : "更改文件夹";
        ToolTipService.SetToolTip(ChooseFolderButton,
            string.IsNullOrWhiteSpace(game.ScreenshotDirectoryPath) ? null : game.ScreenshotDirectoryPath);

        if (string.IsNullOrWhiteSpace(game.ScreenshotDirectoryPath))
        {
            ShowEmptyState("请选择截图文件夹", "选择后将显示该游戏目录中的截图。");
            return;
        }
        if (!Path.IsPathFullyQualified(game.ScreenshotDirectoryPath)
            || !Directory.Exists(game.ScreenshotDirectoryPath))
        {
            ShowEmptyState("无法读取截图文件夹", "目录可能已移动或删除，请重新选择。");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        EmptyState.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        try
        {
            var files = await _catalogService.GetAsync(
                game.ScreenshotDirectoryPath,
                newestFirst: SortSelector.SelectedIndex != 1,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            foreach (var dateGroup in files.GroupBy(file => file.ModifiedAt.LocalDateTime.Date))
            {
                var groupedFiles = dateGroup.ToArray();
                _screenshotCountsByDate[dateGroup.Key] = groupedFiles.Length;
                var group = new ScreenshotDateGroup(dateGroup.Key);
                foreach (var file in groupedFiles)
                {
                    var thumbnail = new BitmapImage { DecodePixelWidth = 560 };
                    thumbnail.UriSource = new Uri(file.Path, UriKind.Absolute);
                    group.Add(new ScreenshotGalleryItem(file.Path, file.FileName, thumbnail));
                }
                _groups.Add(group);
            }

            if (files.Count == 0)
                ShowEmptyState("未找到截图", "该文件夹中没有支持的 PNG、JPG、BMP、GIF 或 WebP 图片。");
            else
            {
                DateJumpPicker.IsEnabled = true;
                EmptyState.Visibility = Visibility.Collapsed;
                ScreenshotGrid.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogService.Instance.AddLog($"[截图] 读取文件夹失败：{ex.Message}");
            ShowEmptyState("无法读取截图文件夹", "目录可能已移动、删除或没有访问权限，请重新选择。");
        }
        finally
        {
            var ownsLoadingState = ReferenceEquals(_loadCancellation, cancellation);
            if (ownsLoadingState) _loadCancellation = null;
            cancellation.Dispose();
            if (ownsLoadingState)
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void ScreenshotGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ScreenshotGalleryItem item) return;
        var image = new Image
        {
            Source = new BitmapImage(new Uri(item.Path, UriKind.Absolute)),
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MaxWidth = 1400,
            MaxHeight = 820,
        };
        var previewRoot = new Grid();
        previewRoot.Children.Add(image);
        var confirmationOverlay = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetZIndex(confirmationOverlay, 10);
        previewRoot.Children.Add(confirmationOverlay);

        var white = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var confirmationContent = new StackPanel { Spacing = 14, Width = 340 };
        confirmationContent.Children.Add(new TextBlock
        {
            Text = "删除这张截图？",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = white,
        });
        confirmationContent.Children.Add(new TextBlock
        {
            Text = $"{item.FileName}\n图片将移动到 Windows 回收站，可以恢复。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = white,
        });
        var confirmationActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancelDelete = new Button { Content = "取消", Foreground = white };
        var confirmDelete = new Button
        {
            Content = "删除",
            Foreground = white,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
        };
        confirmationActions.Children.Add(cancelDelete);
        confirmationActions.Children.Add(confirmDelete);
        confirmationContent.Children.Add(confirmationActions);
        confirmationOverlay.Children.Add(new Border
        {
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(10),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(240, 36, 36, 36)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = confirmationContent,
        });

        var dialog = new ContentDialog
        {
            Content = previewRoot,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
            MaxWidth = 1440,
            MaxHeight = 900,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1440d;
        var contextMenu = new MenuFlyout();
        var showInFolder = new MenuFlyoutItem
        {
            Text = "在文件夹中显示",
            Icon = new FontIcon { Glyph = "\uE838" },
        };
        showInFolder.Click += (_, _) => ShowInFolder(item.Path);
        contextMenu.Items.Add(showInFolder);
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        var delete = new MenuFlyoutItem
        {
            Text = "删除截图",
            Icon = new FontIcon { Glyph = "\uE74D" },
        };
        cancelDelete.Click += (_, _) => confirmationOverlay.Visibility = Visibility.Collapsed;
        confirmDelete.Click += async (_, _) =>
        {
            confirmDelete.IsEnabled = false;
            cancelDelete.IsEnabled = false;
            if (await DeleteScreenshotFileAsync(item))
            {
                confirmationOverlay.Visibility = Visibility.Collapsed;
                dialog.Hide();
            }
            else
            {
                confirmDelete.IsEnabled = true;
                cancelDelete.IsEnabled = true;
            }
        };
        delete.Click += (_, _) => confirmationOverlay.Visibility = Visibility.Visible;
        contextMenu.Items.Add(delete);
        image.ContextFlyout = contextMenu;
        ToolTipService.SetToolTip(image, "右键可定位或删除此截图");
        await dialog.ShowAsync();
    }

    private void DateJumpPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_loadingCalendar || args.NewDate is not { } selectedDate) return;
        var date = selectedDate.LocalDateTime.Date;
        var group = _groups.FirstOrDefault(candidate => candidate.Date == date);
        if (group is null || group.Count == 0)
        {
            ToolTipService.SetToolTip(DateJumpPicker, $"{date:yyyy-MM-dd} 没有截图");
            return;
        }

        ToolTipService.SetToolTip(DateJumpPicker, $"跳转到 {date:yyyy-MM-dd}");
        DispatcherQueue.TryEnqueue(() =>
            ScreenshotGrid.ScrollIntoView(group[0], ScrollIntoViewAlignment.Leading));
    }

    private void DateJumpPicker_CalendarViewDayItemChanging(
        CalendarView sender,
        CalendarViewDayItemChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        var date = args.Item.Date.LocalDateTime.Date;
        var hasScreenshots = _screenshotCountsByDate.TryGetValue(date, out var count);

        // 日期项会被虚拟化复用，因此无论有无截图都必须完整重置状态。
        args.Item.IsBlackout = !hasScreenshots;
        args.Item.SetDensityColors(hasScreenshots
            ? [Color.FromArgb(255, 0, 120, 212)]
            : Array.Empty<Color>());
        ToolTipService.SetToolTip(args.Item,
            hasScreenshots ? $"{date:yyyy-MM-dd} · {count} 张截图" : $"{date:yyyy-MM-dd} · 无截图");
    }

    private static void ShowInFolder(string path)
    {
        if (!File.Exists(path))
        {
            LogService.Instance.AddLog($"[截图] 无法在文件夹中显示，文件不存在：{path}");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[截图] 打开文件位置失败：{ex.Message}");
        }
    }

    private void ShowScreenshotInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ScreenshotGalleryItem item })
            ShowInFolder(item.Path);
    }

    private async void DeleteScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ScreenshotGalleryItem item }) return;
        await ConfirmAndDeleteScreenshotAsync(item);
    }

    private async Task ConfirmAndDeleteScreenshotAsync(ScreenshotGalleryItem item)
    {
        var confirmation = new ContentDialog
        {
            Title = "删除这张截图？",
            Content = $"{item.FileName}\n\n图片将移动到 Windows 回收站，可以恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        await DeleteScreenshotFileAsync(item);
    }

    private async Task<bool> DeleteScreenshotFileAsync(ScreenshotGalleryItem item)
    {
        try
        {
            if (File.Exists(item.Path))
            {
                FileSystem.DeleteFile(
                    item.Path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            await LoadSelectedGameAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            LogService.Instance.AddLog($"[截图] 删除截图失败：{ex.Message}");
            return false;
        }
    }

    private void ScreenshotGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ScreenshotGrid.ItemsPanelRoot is not ItemsWrapGrid panel || e.NewSize.Width <= 0) return;
        const double columnCount = 4;
        const double reservedScrollbarWidth = 14;
        const double minimumSlotWidth = 220;
        var slotWidth = Math.Max(
            minimumSlotWidth,
            Math.Floor((e.NewSize.Width - reservedScrollbarWidth) / columnCount));
        // GridViewItem 的右/下边距均为 10；扣除后让实际卡片保持 16:9。
        panel.ItemWidth = slotWidth;
        panel.ItemHeight = Math.Floor((slotWidth - 10) * 9 / 16) + 10;
    }

    private void ShowEmptyState(string title, string detail)
    {
        EmptyStateTitle.Text = title;
        EmptyStateDetail.Text = detail;
        EmptyState.Visibility = Visibility.Visible;
        ScreenshotGrid.Visibility = Visibility.Collapsed;
    }

    private void CancelLoading()
    {
        var previous = Interlocked.Exchange(ref _loadCancellation, null);
        previous?.Cancel();
    }

    private sealed record ScreenshotGalleryItem(
        string Path,
        string FileName,
        BitmapImage Thumbnail);

    private sealed class ScreenshotDateGroup(DateTime date)
        : ObservableCollection<ScreenshotGalleryItem>
    {
        public DateTime Date { get; } = date;
        public string DateText { get; } = date.ToString("yyyy-MM-dd");
    }
}
