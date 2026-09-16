using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class AppearanceSettingsPage : Page
{
    private readonly AppearanceService _service = AppearanceService.Instance;
    // 滑块实时预览，停止调整后再写配置；离开页面时补齐尚未保存的变更。
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _loading = true;
    private bool _active;
    private bool _busy;
    private int _galleryRequest;
    private string _selectedPageId = AppearancePageIds.Home;

    private AppearanceProfile EditingProfile => _service.Settings.UsePerPageSettings
        ? _service.Settings.GetEffective(_selectedPageId)
        : _service.Settings;

    public sealed class GalleryImage
    {
        public string FileName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public BitmapImage? Thumbnail { get; init; }
    }

    public AppearanceSettingsPage()
    {
        InitializeComponent();
        LoadInterfaceOptions();
        _saveTimer.Tick += (_, _) => SaveNow();
        Loaded += async (_, _) => { _active = true; await RefreshGalleryAsync(); };
        Unloaded += (_, _) => { _active = false; ++_galleryRequest; if (_saveTimer.IsEnabled) SaveNow(); };
    }

    private void Report(string message, bool error = true)
    {
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        Status.IsOpen = true;
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        if (!_service.Save()) Report("外观设置保存失败，当前预览仍有效，但重启后可能丢失。请检查配置目录是否可写。");
    }

    private async Task RefreshGalleryAsync()
    {
        var request = ++_galleryRequest;
        _loading = true;
        try
        {
            var images = new List<GalleryImage>();
            foreach (var name in _service.GetImages())
            {
                BitmapImage? thumbnail = null;
                try { thumbnail = await _service.LoadImageAsync(name, 240); }
                catch { /* Keep broken entries visible so they can be deleted. */ }
                if (!_active || request != _galleryRequest) return;
                var stem = Path.GetFileNameWithoutExtension(name);
                if (stem.Length > 33 && Guid.TryParseExact(stem[^32..], "N", out _)) stem = stem[..^33];
                images.Add(new GalleryImage { FileName = name, DisplayName = thumbnail == null ? $"无法读取：{stem}" : stem, Thumbnail = thumbnail });
            }
            if (!_active || request != _galleryRequest) return;
            Gallery.ItemsSource = images;
            var profile = EditingProfile;
            Gallery.SelectedItem = images.FirstOrDefault(i => i.FileName == profile.SelectedImage);
            EmptyHint.Visibility = images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (profile.Enabled && (Gallery.SelectedItem is not GalleryImage selected || selected.Thumbnail == null))
                Report("所选背景已丢失或无法读取，已显示默认背景。请重新选择图片。");
            LoadOptions();
        }
        catch (Exception ex) { Report($"无法读取图片文件夹：{ex.Message}"); }
        finally { if (request == _galleryRequest) _loading = false; }
    }

    private void LoadOptions()
    {
        _loading = true;
        LoadInterfaceOptions();
        var profile = EditingProfile;
        var options = profile.Current;
        BackgroundEnabled.IsOn = profile.Enabled;
        ImageOpacity.Value = double.IsFinite(options.Opacity) ? Math.Clamp(options.Opacity * 100, 0, 100) : 60;
        OverlayOpacity.Value = double.IsFinite(options.OverlayOpacity) ? Math.Clamp(options.OverlayOpacity * 100, 0, 100) : 25;
        StretchMode.SelectedIndex = options.Stretch switch { "Uniform" => 1, "Fill" => 2, _ => 0 };
        DeleteButton.IsEnabled = Gallery.SelectedItem != null && !_busy;
        RecropButton.IsEnabled = Gallery.SelectedItem != null && !_busy;
        var hasImage = Gallery.SelectedItem is GalleryImage { Thumbnail: not null };
        ImageOpacity.IsEnabled = OverlayOpacity.IsEnabled = StretchMode.IsEnabled = hasImage;
        if (!string.IsNullOrEmpty(options.SourceImage)) StretchMode.IsEnabled = false;
        BackgroundEnabled.IsEnabled = hasImage || BackgroundEnabled.IsOn;
        UpdateLabels();
        _loading = false;
    }

    private void UpdateLabels()
    {
        OpacityLabel.Text = $"图片不透明度：{ImageOpacity.Value:0}%";
        OverlayLabel.Text = $"遮罩强度：{OverlayOpacity.Value:0}%";
    }

    private void LoadInterfaceOptions()
    {
        BackgroundScope.SelectedIndex = _service.Settings.UsePerPageSettings ? 1 : 0;
        PageSelectorPanel.Visibility = _service.Settings.UsePerPageSettings ? Visibility.Visible : Visibility.Collapsed;
        TargetPage.SelectedIndex = _selectedPageId switch
        {
            AppearancePageIds.GameConfiguration => 1,
            AppearancePageIds.Screenshots => 2,
            AppearancePageIds.Appearance => 3,
            AppearancePageIds.Settings => 4,
            _ => 0,
        };
        var profile = EditingProfile;
        var value = profile.CardOpacity;
        var defaultColor = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]).Color;
        CardOpacity.Value = value is double opacity && double.IsFinite(opacity)
            ? Math.Clamp(opacity * 100, 0, 100) : Math.Round(defaultColor.A / 255d * 100);
        CardOpacityLabel.Text = $"内容卡片不透明度：{CardOpacity.Value:0}%";
        ShowHomeAnimation.IsOn = profile.ShowHomeAnimation;
        ShowHomeNews.IsOn = profile.ShowHomeNews;
        var isHomeSettings = !_service.Settings.UsePerPageSettings || _selectedPageId == AppearancePageIds.Home;
        HomeContentCard.Visibility = isHomeSettings ? Visibility.Visible : Visibility.Collapsed;
        var pageName = (TargetPage.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "首页";
        InterfaceAdjustmentTitle.Text = _service.Settings.UsePerPageSettings ? $"界面调节 · {pageName}" : "界面调节";
        BackgroundImageTitle.Text = _service.Settings.UsePerPageSettings ? $"背景图片 · {pageName}" : "背景图片";
    }

    private void CardOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        EditingProfile.CardOpacity = CardOpacity.Value / 100;
        CardOpacityLabel.Text = $"内容卡片不透明度：{CardOpacity.Value:0}%";
        _service.Preview();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ShowHomeNews_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        EditingProfile.ShowHomeNews = ShowHomeNews.IsOn;
        PreviewAndScheduleSave();
    }

    private void ShowHomeAnimation_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        EditingProfile.ShowHomeAnimation = ShowHomeAnimation.IsOn;
        PreviewAndScheduleSave();
    }

    private async void BackgroundScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var usePerPage = (BackgroundScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "per-page";
        if (usePerPage)
            _service.Settings.InitializePerPageSettings();
        _service.Settings.UsePerPageSettings = usePerPage;
        _service.Preview();
        SaveNow();
        await RefreshGalleryAsync();
    }

    private async void TargetPage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TargetPage.SelectedItem is not ComboBoxItem item) return;
        _selectedPageId = AppearancePageIds.Normalize(item.Tag?.ToString());
        await RefreshGalleryAsync();
    }

    private void PreviewAndScheduleSave()
    {
        _service.Preview();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OptionsChanged(object sender, RoutedEventArgs e) => ApplyOptions();
    private void SliderChanged(object sender, RangeBaseValueChangedEventArgs e) => ApplyOptions();
    private void StretchChanged(object sender, SelectionChangedEventArgs e) => ApplyOptions();

    private void ApplyOptions()
    {
        if (_loading) return;
        var profile = EditingProfile;
        profile.Enabled = BackgroundEnabled.IsOn;
        var options = profile.Current;
        options.Opacity = ImageOpacity.Value / 100;
        options.OverlayOpacity = OverlayOpacity.Value / 100;
        options.Stretch = (StretchMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "UniformToFill";
        UpdateLabels();
        _service.Preview();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Gallery.SelectedItem is not GalleryImage item) return;
        var profile = EditingProfile;
        profile.SelectedImage = item.FileName;
        profile.Enabled = item.Thumbnail != null;
        LoadOptions();
        _service.Preview();
        SaveNow();
        if (item.Thumbnail == null) Report("这张图片无法读取，请删除后重新导入。");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ImportButton.IsEnabled = DeleteButton.IsEnabled = RecropButton.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var source = await picker.PickSingleFileAsync();
            if (source != null && _active) await CropAndSaveAsync(source);
        }
        catch (Exception ex) { Report($"导入裁切失败：{ex.Message}"); }
        finally { _busy = false; ImportButton.IsEnabled = true; LoadOptions(); }
    }

    private async void Recrop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Gallery.SelectedItem is not GalleryImage item) return;
        _busy = true;
        ImportButton.IsEnabled = DeleteButton.IsEnabled = RecropButton.IsEnabled = false;
        try
        {
            EditingProfile.Images.TryGetValue(item.FileName, out var previous);
            var path = !string.IsNullOrEmpty(previous?.SourceImage)
                ? _service.GetOriginalPath(previous.SourceImage) : _service.GetImagePath(item.FileName);
            var source = await StorageFile.GetFileFromPathAsync(path);
            await CropAndSaveAsync(source, previous);
        }
        catch (Exception ex) { Report($"无法重新裁切，请确认保存的原图仍存在：{ex.Message}"); }
        finally { _busy = false; ImportButton.IsEnabled = true; LoadOptions(); }
    }

    private async Task CropAndSaveAsync(StorageFile source,
        SteamCNGameLauncher.Models.BackgroundOptions? previous = null)
    {
        var image = await BackgroundCropImage.LoadAsync(source);
        if (!_active) return;
        var dialog = new Views.Dialogs.BackgroundCropDialog(image, previous) { XamlRoot = XamlRoot };
        // 取消只丢弃内存预览；确认后才复制原图、生成背景并切换当前选择。
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var profile = EditingProfile;
        var name = await _service.SaveCropAsync(source, image, dialog.ImageScale, dialog.OffsetX, dialog.OffsetY, profile, previous);
        profile.SelectedImage = name;
        profile.Enabled = true;
        _service.Preview();
        await RefreshGalleryAsync();
        Report("已保存并应用裁切背景，未覆盖区域已填充白色。", false);
        SaveNow();
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Gallery.SelectedItem is not GalleryImage item) return;
        _busy = true;
        ImportButton.IsEnabled = DeleteButton.IsEnabled = false;
        try
        {
            var dialog = new ContentDialog { Title = "删除背景图片", Content = "将从所有界面的背景选择中移除该图片，并删除软件图片库中的副本；你电脑上的原始图片不会被删除。",
                PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _service.Delete(item.FileName);
            await RefreshGalleryAsync();
            SaveNow();
        }
        catch (Exception ex) { Report($"删除失败：{ex.Message}"); }
        finally { _busy = false; ImportButton.IsEnabled = true; LoadOptions(); }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_service.ImageDirectory);
            var folder = await StorageFolder.GetFolderFromPathAsync(_service.ImageDirectory);
            if (!await Windows.System.Launcher.LaunchFolderAsync(folder)) Report("无法打开图片文件夹。");
        }
        catch (Exception ex) { Report($"无法打开图片文件夹：{ex.Message}"); }
    }
}
