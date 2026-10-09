using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Media.Playback;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// Owns the single, stable XAML output surface used by every home video. MediaPlayer
/// instances only decode frames; changing games or looping never replaces the visual.
/// </summary>
public sealed class HomeVideoFrameRenderer : IDisposable
{
    public const int OutputWidth = 1920;
    public const int OutputHeight = 1080;

    private readonly CanvasDevice _device;
    private readonly CanvasImageSource _output;
    private readonly CanvasRenderTarget?[] _frames = new CanvasRenderTarget?[2];
    private readonly uint[] _frameWidths = new uint[2];
    private readonly uint[] _frameHeights = new uint[2];
    private CanvasRenderTarget? _loopHold;
    private bool _disposed;

    public HomeVideoFrameRenderer(Image target)
    {
        _device = CanvasDevice.GetSharedDevice();
        _output = new CanvasImageSource(_device, OutputWidth, OutputHeight, 96);
        target.Source = _output;
        ClearOutput();
    }

    public bool CopyFrame(MediaPlayer player, int slot, uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)slot >= _frames.Length || width == 0 || height == 0) return false;

        if (_frames[slot] is null || _frameWidths[slot] != width || _frameHeights[slot] != height)
        {
            _frames[slot]?.Dispose();
            _frames[slot] = new CanvasRenderTarget(_device, width, height, 96);
            _frameWidths[slot] = width;
            _frameHeights[slot] = height;
        }

        player.CopyFrameToVideoSurface(_frames[slot]);
        return true;
    }

    public void RenderSingle(int slot)
    {
        if (_disposed) return;
        using var drawing = _output.CreateDrawingSession(Colors.Black);
        DrawCover(drawing, _frames.ElementAtOrDefault(slot), 1f);
    }

    public void RenderTransition(int oldSlot, int newSlot, float progress)
    {
        if (_disposed) return;
        progress = Math.Clamp(progress, 0f, 1f);
        using var drawing = _output.CreateDrawingSession(Colors.Black);
        DrawCover(drawing, _frames.ElementAtOrDefault(newSlot), 1f);
        DrawCover(drawing, _frames.ElementAtOrDefault(oldSlot), 1f - progress);
    }

    public bool CaptureLoopHold(int slot)
    {
        if (_disposed || _frames.ElementAtOrDefault(slot) is null) return false;
        _loopHold ??= new CanvasRenderTarget(_device, OutputWidth, OutputHeight, 96);
        using var drawing = _loopHold.CreateDrawingSession();
        drawing.Clear(Colors.Black);
        DrawCover(drawing, _frames[slot], 1f);
        return true;
    }

    public void RenderLoopTransition(int slot, float progress)
    {
        if (_disposed) return;
        progress = Math.Clamp(progress, 0f, 1f);
        using var drawing = _output.CreateDrawingSession(Colors.Black);
        DrawCover(drawing, _frames.ElementAtOrDefault(slot), 1f);
        if (_loopHold is not null)
            drawing.DrawImage(_loopHold, new Rect(0, 0, OutputWidth, OutputHeight),
                new Rect(0, 0, OutputWidth, OutputHeight), 1f - progress);
    }

    public void ClearSlot(int slot)
    {
        if ((uint)slot >= _frames.Length) return;
        _frames[slot]?.Dispose();
        _frames[slot] = null;
        _frameWidths[slot] = 0;
        _frameHeights[slot] = 0;
    }

    public void ClearOutput()
    {
        if (_disposed) return;
        using var drawing = _output.CreateDrawingSession(Colors.Black);
    }

    private static void DrawCover(CanvasDrawingSession drawing, CanvasRenderTarget? frame, float opacity)
    {
        if (frame is null || opacity <= 0) return;
        var sourceWidth = frame.SizeInPixels.Width;
        var sourceHeight = frame.SizeInPixels.Height;
        if (sourceWidth == 0 || sourceHeight == 0) return;

        var outputRatio = (double)OutputWidth / OutputHeight;
        var sourceRatio = (double)sourceWidth / sourceHeight;
        Rect source;
        if (sourceRatio > outputRatio)
        {
            var width = sourceHeight * outputRatio;
            source = new Rect((sourceWidth - width) / 2d, 0, width, sourceHeight);
        }
        else
        {
            var height = sourceWidth / outputRatio;
            source = new Rect(0, (sourceHeight - height) / 2d, sourceWidth, height);
        }

        drawing.DrawImage(frame, new Rect(0, 0, OutputWidth, OutputHeight), source, opacity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var frame in _frames) frame?.Dispose();
        _loopHold?.Dispose();
        // CanvasImageSource is managed by XAML and the shared CanvasDevice may be used by
        // other Win2D consumers; only renderer-owned render targets are disposable here.
    }
}
