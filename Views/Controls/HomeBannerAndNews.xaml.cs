using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Views.Controls;

/// <summary>
/// 使用统一 HomeContent 展示 Banner 与资讯。控件不感知厂商 Provider 或传输方式。
/// </summary>
public sealed partial class HomeBannerAndNews : UserControl
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _bannerTimer;

    public ObservableCollection<HomeBannerDisplayItem> BannerItems { get; } = [];
    public ObservableCollection<HomeNewsGroup> NewsGroups { get; } = [];
    public ObservableCollection<HomeNewsDisplayItem> SelectedNewsItems { get; } = [];

    private HomeNewsGroup? _selectedNewsGroup;

    public double BannerHeight
    {
        get => BannerRow.Height.Value;
        set => BannerRow.Height = new GridLength(Math.Max(0, value));
    }

    public double LayoutWidth
    {
        get => DesignSurface.Width;
        set => DesignSurface.Width = Math.Max(1, value);
    }

    public double LayoutHeight
    {
        get => DesignSurface.Height;
        set => DesignSurface.Height = Math.Max(1, value);
    }

    public HomeBannerAndNews()
    {
        InitializeComponent();
        _bannerTimer = DispatcherQueue.CreateTimer();
        _bannerTimer.Interval = TimeSpan.FromSeconds(5);
        _bannerTimer.IsRepeating = true;
        _bannerTimer.Tick += BannerTimer_Tick;
        Loaded += HomeBannerAndNews_Loaded;
        Unloaded += HomeBannerAndNews_Unloaded;
        SetContent(new HomeContent());
    }

    public void SetContent(HomeContent content)
    {
        _bannerTimer.Stop();
        BannerItems.Clear();
        foreach (var banner in content.Banners)
        {
            BannerItems.Add(new HomeBannerDisplayItem(
                banner.Id,
                string.IsNullOrWhiteSpace(banner.Title) ? "查看详情" : banner.Title,
                CreateImageSource(banner.LocalPath, banner.ImageUrl),
                banner.TargetUrl));
        }

        BuildNewsGroups(content.News);
        SelectNewsGroup(NewsGroups.FirstOrDefault());

        BannerFlipView.SelectedIndex = BannerItems.Count > 0 ? 0 : -1;
        BannerFlipView.Visibility = BannerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BannerEmptyState.Visibility = BannerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerCounter();
        StartBannerTimerIfNeeded();
    }

    private void BuildNewsGroups(IReadOnlyList<HomeNewsItem> news)
    {
        NewsGroups.Clear();
        var groupsByKey = new Dictionary<string, HomeNewsGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in news)
        {
            var (key, header) = ResolveCategory(item.Category);
            if (!groupsByKey.TryGetValue(key, out var group))
            {
                group = new HomeNewsGroup(key, header);
                groupsByKey.Add(key, group);
                NewsGroups.Add(group);
            }

            group.Items.Add(new HomeNewsDisplayItem(
                item.Id,
                item.Title,
                item.PublishedAt?.ToLocalTime().ToString("MM/dd") ?? string.Empty,
                item.TargetUrl));
        }

        if (NewsGroups.Count == 0)
            NewsGroups.Add(new HomeNewsGroup("information", "资讯"));
    }

    private static (string Key, string Header) ResolveCategory(string? category)
    {
        var value = category?.Trim();
        return value?.ToLowerInvariant() switch
        {
            "activity" or "event" or "events" or "活动" => ("activity", "活动"),
            "announce" or "announcement" or "notice" or "公告" => ("announcement", "公告"),
            "info" or "information" or "news" or "资讯" or "新闻" => ("information", "资讯"),
            null or "" => ("information", "资讯"),
            _ => ($"custom:{value.ToLowerInvariant()}", value)
        };
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeNewsGroup group })
            SelectNewsGroup(group);
    }

    private void CategoryScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (CategoryScrollViewer.ScrollableWidth <= 0)
            return;

        var delta = e.GetCurrentPoint(CategoryScrollViewer).Properties.MouseWheelDelta;
        CategoryScrollViewer.ChangeView(
            Math.Clamp(CategoryScrollViewer.HorizontalOffset - delta, 0, CategoryScrollViewer.ScrollableWidth),
            null,
            null,
            true);
        e.Handled = true;
    }

    private void SelectNewsGroup(HomeNewsGroup? group)
    {
        _selectedNewsGroup = group;
        foreach (var item in NewsGroups)
            item.IsSelected = ReferenceEquals(item, group);

        SelectedNewsItems.Clear();
        if (group is not null)
        {
            foreach (var item in group.Items)
                SelectedNewsItems.Add(item);
        }
        NewsEmptyState.Visibility = SelectedNewsItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ImageSource? CreateImageSource(string? localPath, string? imageUrl)
    {
        var source = !string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath)
            ? localPath
            : imageUrl;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;
    }

    private void HomeBannerAndNews_Loaded(object sender, RoutedEventArgs e) => StartBannerTimerIfNeeded();

    private void HomeBannerAndNews_Unloaded(object sender, RoutedEventArgs e) => _bannerTimer.Stop();

    private void StartBannerTimerIfNeeded()
    {
        if (IsLoaded && BannerItems.Count > 1)
            _bannerTimer.Start();
    }

    private void BannerTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (BannerItems.Count > 1)
            BannerFlipView.SelectedIndex = (BannerFlipView.SelectedIndex + 1) % BannerItems.Count;
    }

    private void BannerContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _bannerTimer.Stop();
        BannerCounterBorder.Visibility = BannerItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BannerContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        BannerCounterBorder.Visibility = Visibility.Collapsed;
        StartBannerTimerIfNeeded();
    }

    private void BannerFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBannerCounter();

    private void UpdateBannerCounter()
    {
        BannerCounter.Text = BannerItems.Count == 0
            ? string.Empty
            : $"{Math.Max(0, BannerFlipView.SelectedIndex) + 1}/{BannerItems.Count}";
    }

    /// <summary>隐藏 FlipView 默认翻页按钮，保持与 Starward 相同的简洁轮播样式。</summary>
    private void BannerFlipView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (VisualTreeHelper.GetChildrenCount(BannerFlipView) == 0)
                return;
            var root = VisualTreeHelper.GetChild(BannerFlipView, 0);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                if (VisualTreeHelper.GetChild(root, i) is Button button)
                {
                    button.IsHitTestVisible = false;
                    button.Opacity = 0;
                }
            }
        }
        catch
        {
            // 控件模板在 Windows App SDK 更新后可能改变；失败时保留系统默认翻页按钮。
        }
    }

    private async void Banner_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeBannerDisplayItem item })
            await OpenHttpsAsync(item.TargetUrl);
    }

    private async void NewsItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeNewsDisplayItem item })
            await OpenHttpsAsync(item.TargetUrl);
    }

    private static async Task OpenHttpsAsync(string? targetUrl)
    {
        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}

// WinUI 的生成类型系统需要可写属性，不能使用 positional record 的 init-only 属性。
public sealed class HomeBannerDisplayItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public ImageSource? ImageSource { get; set; }
    public string? TargetUrl { get; set; }

    public HomeBannerDisplayItem() { }

    public HomeBannerDisplayItem(string id, string title, ImageSource? imageSource, string? targetUrl) =>
        (Id, Title, ImageSource, TargetUrl) = (id, title, imageSource, targetUrl);
}

public sealed class HomeNewsDisplayItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string DateText { get; set; } = "";
    public string? TargetUrl { get; set; }

    public HomeNewsDisplayItem() { }

    public HomeNewsDisplayItem(string id, string title, string dateText, string? targetUrl) =>
        (Id, Title, DateText, TargetUrl) = (id, title, dateText, targetUrl);
}

public sealed class HomeNewsGroup : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Key { get; set; } = "";
    public string Header { get; set; } = "";
    public ObservableCollection<HomeNewsDisplayItem> Items { get; set; } = [];
    public double HeaderOpacity => IsSelected ? 1 : 0.62;
    public Visibility SelectionIndicatorVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderOpacity));
            OnPropertyChanged(nameof(SelectionIndicatorVisibility));
        }
    }

    public HomeNewsGroup() { }

    public HomeNewsGroup(string key, string header) => (Key, Header) = (key, header);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
