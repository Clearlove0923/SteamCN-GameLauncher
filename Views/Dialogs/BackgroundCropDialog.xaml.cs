using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher.Views.Dialogs;

/// <summary>固定输出画布，Viewbox 仅缩放预览显示，不改变保存的裁切坐标。</summary>
public sealed partial class BackgroundCropDialog : ContentDialog
{
    private readonly BackgroundCropImage _image;
    private readonly double _coverScale;
    private readonly double _minimumScale;
    private bool _loading = true;
    private Pointer? _pointer;
    private Point _lastPoint;
    public double ImageScale { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public BackgroundCropDialog(BackgroundCropImage image, BackgroundOptions? previous = null)
    {
        _image = image;
        _coverScale = Math.Max((double)BackgroundCropRenderer.Width / image.Width,
            (double)BackgroundCropRenderer.Height / image.Height);
        _minimumScale = Math.Min(_coverScale * .05, Math.Min(
            (double)BackgroundCropRenderer.Width / image.Width,
            (double)BackgroundCropRenderer.Height / image.Height));
        InitializeComponent();
        Title = $"裁切背景 · {BackgroundCropRenderer.Width} × {BackgroundCropRenderer.Height}（16:9）";
        CropCanvas.Width = BackgroundCropRenderer.Width;
        CropCanvas.Height = BackgroundCropRenderer.Height;
        CropClip.Rect = new Rect(0, 0, BackgroundCropRenderer.Width, BackgroundCropRenderer.Height);
        ZoomSlider.Minimum = _minimumScale / _coverScale * 100;
        Resources["ContentDialogMaxWidth"] = 860d;
        CropImage.Source = image.Preview;
        ImageScale = _coverScale;
        if (previous != null && double.IsFinite(previous.CropScale) && previous.CropScale > 0
            && double.IsFinite(previous.CropX) && double.IsFinite(previous.CropY))
        {
            var oldWidth = previous.CropWidth > 0 ? previous.CropWidth : 1440;
            var oldHeight = previous.CropHeight > 0 ? previous.CropHeight : 810;
            var migrationScaleX = (double)BackgroundCropRenderer.Width / oldWidth;
            var migrationScaleY = (double)BackgroundCropRenderer.Height / oldHeight;
            var migrationScale = Math.Min(migrationScaleX, migrationScaleY);
            ImageScale = Math.Clamp(previous.CropScale * migrationScale, _minimumScale, _coverScale * 4);
            OffsetX = previous.CropX * migrationScaleX;
            OffsetY = previous.CropY * migrationScaleY;
        }
        else Center();
        Draw();
        _loading = false;
    }

    private void Draw()
    {
        CropImage.Width = _image.Width * ImageScale;
        CropImage.Height = _image.Height * ImageScale;
        Canvas.SetLeft(CropImage, OffsetX);
        Canvas.SetTop(CropImage, OffsetY);
        var loading = _loading;
        _loading = true;
        ZoomSlider.Value = ImageScale / _coverScale * 100;
        ZoomLabel.Text = $"缩放：{ZoomSlider.Value:0}%（100% 为铺满）";
        _loading = loading;
    }

    private void Zoom(double scale, Point anchor)
    {
        // 缩放时保持锚点下的源像素不动：滚轮使用鼠标位置，滑块使用画布中心。
        scale = Math.Clamp(scale, _minimumScale, _coverScale * 4);
        var ratio = scale / ImageScale;
        OffsetX = anchor.X - (anchor.X - OffsetX) * ratio;
        OffsetY = anchor.Y - (anchor.Y - OffsetY) * ratio;
        ImageScale = scale;
        Draw();
    }

    private void Center()
    {
        OffsetX = (BackgroundCropRenderer.Width - _image.Width * ImageScale) / 2;
        OffsetY = (BackgroundCropRenderer.Height - _image.Height * ImageScale) / 2;
    }

    private void Zoom_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_loading) Zoom(_coverScale * e.NewValue / 100,
            new Point(BackgroundCropRenderer.Width / 2d, BackgroundCropRenderer.Height / 2d));
    }
    private void Cover_Click(object sender, RoutedEventArgs e) { ImageScale = _coverScale; Center(); Draw(); }
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        ImageScale = Math.Min((double)BackgroundCropRenderer.Width / _image.Width,
            (double)BackgroundCropRenderer.Height / _image.Height);
        Center(); Draw();
    }
    private void Center_Click(object sender, RoutedEventArgs e) { Center(); Draw(); }
    private void Canvas_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer != null || (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !e.GetCurrentPoint(CropCanvas).Properties.IsLeftButtonPressed)) return;
        if (!CropCanvas.CapturePointer(e.Pointer)) return;
        _pointer = e.Pointer;
        _lastPoint = e.GetCurrentPoint(CropCanvas).Position;
        e.Handled = true;
    }
    private void Canvas_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(CropCanvas).Position;
        OffsetX += point.X - _lastPoint.X;
        OffsetY += point.Y - _lastPoint.Y;
        _lastPoint = point;
        Draw();
        e.Handled = true;
    }
    private void Canvas_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        _pointer = null;
        CropCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
    private void Canvas_CaptureLost(object sender, PointerRoutedEventArgs e) => _pointer = null;
    private void Canvas_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CropCanvas);
        Zoom(ImageScale * Math.Pow(1.1, point.Properties.MouseWheelDelta / 120d), point.Position);
        e.Handled = true;
    }
}
