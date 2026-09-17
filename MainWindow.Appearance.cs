using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher;

public sealed partial class MainWindow
{
    private readonly AppearanceService _appearance = AppearanceService.Instance;
    private string _loadedBackground = "";
    private int _backgroundRequest;
    private readonly SolidColorBrush _paneBrush = new();
    private readonly SolidColorBrush _expandedPaneBrush = new();
    private readonly SolidColorBrush _contentBrush = new();

    private void InitializeAppearance()
    {
        NavView.Resources["NavigationViewDefaultPaneBackground"] = _paneBrush;
        NavView.Resources["NavigationViewExpandedPaneBackground"] = _expandedPaneBrush;
        NavView.Resources["NavigationViewContentBackground"] = _contentBrush;
        _appearance.Changed += ApplyAppearance;
        WindowRoot.ActualThemeChanged += (_, _) => ApplyAppearance();
        Closed += (_, _) => { _appearance.Changed -= ApplyAppearance; ++_backgroundRequest; };
        ApplyAppearance();
    }

    private async void ApplyAppearance()
    {
        // 解码可能晚于下一次切图完成，仅允许最后一次请求更新背景。
        var request = ++_backgroundRequest;
        var settings = _appearance.CurrentProfile;
        var cardBrush = (SolidColorBrush)Application.Current.Resources["AppearanceCardBackground"];
        var cardColor = ((SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]).Color;
        if (settings.CardOpacity is double opacity && double.IsFinite(opacity))
            cardColor.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        // 只改变底色画刷，卡片内的文字与输入控件不会一起变透明。
        cardBrush.Color = cardColor;
        var enabled = settings.Enabled && !string.IsNullOrEmpty(settings.SelectedImage);
        if (enabled && _loadedBackground != settings.SelectedImage)
        {
            var name = settings.SelectedImage;
            try
            {
                var bitmap = await _appearance.LoadImageAsync(name, 1920);
                if (request != _backgroundRequest) return;
                BackgroundImage.Source = bitmap;
                _loadedBackground = name;
            }
            catch (Exception ex)
            {
                if (request != _backgroundRequest) return;
                enabled = false;
                LogService.Instance.AddLog($"背景图片无法加载，已恢复默认外观：{ex.Message}");
            }
        }
        if (!enabled)
        {
            BackgroundImage.Source = null;
            _loadedBackground = "";
        }
        BackgroundImage.Visibility = BackgroundOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        var options = settings.Current;
        BackgroundImage.Opacity = double.IsFinite(options.Opacity) ? Math.Clamp(options.Opacity, 0, 1) : 0.6;
        BackgroundOverlay.Opacity = double.IsFinite(options.OverlayOpacity) ? Math.Clamp(options.OverlayOpacity, 0, 1) : 0.25;
        BackgroundImage.Stretch = options.Stretch switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
        var pageBrush = (SolidColorBrush)Application.Current.Resources["AppearancePageBackground"];
        var defaultPage = (SolidColorBrush)Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"];
        pageBrush.Color = enabled ? Colors.Transparent : defaultPage.Color;
        _contentBrush.Color = enabled ? Colors.Transparent
            : ((SolidColorBrush)Application.Current.Resources["NavigationViewContentBackground"]).Color;
        // 侧栏保持稳定的深灰底色，避免背景图片影响导航图标辨识度。
        var paneColor = Windows.UI.Color.FromArgb(235, 55, 55, 55);
        _paneBrush.Color = paneColor;
        _expandedPaneBrush.Color = paneColor;
    }
}
