using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher;

public sealed partial class MainWindow
{
    private readonly AppearanceService _appearance = AppearanceService.Instance;
    private string _loadedBackground = "";
    private int _backgroundRequest;
    private readonly SolidColorBrush _paneBrush = new();
    private readonly SolidColorBrush _expandedPaneBrush = new();
    private readonly SolidColorBrush _contentBrush = new();
    private readonly HomeBackdropCoordinator _homeBackdrop = HomeBackdropCoordinator.Instance;
    private readonly MediaPlayer?[] _videoPlayers = new MediaPlayer?[2];
    private readonly string[] _videoKeys = ["", ""];
    private readonly string[] _videoSources = ["", ""];
    private CancellationTokenSource? _videoOptimizationCancellation;
    private string _optimizingSource = "";
    private string _optimizingKey = "";
    private MediaPlayerElement[] _videoElements = null!;
    private HomeBackdropState _homeBackdropState = HomeBackdropState.Inactive;
    private int _activeVideoSlot = -1;
    private int _pendingVideoSlot = -1;
    private int _videoRequest;
    private DispatcherTimer? _pendingVideoTimer;
    private Storyboard? _videoTransition;
    private bool _closingHomeMedia;

    private void InitializeAppearance()
    {
        _paneBrush.Color = Colors.Transparent;
        _expandedPaneBrush.Color = Colors.Transparent;
        NavView.Resources["NavigationViewDefaultPaneBackground"] = _paneBrush;
        NavView.Resources["NavigationViewExpandedPaneBackground"] = _expandedPaneBrush;
        NavView.Resources["NavigationViewContentBackground"] = _contentBrush;
        // NavigationView 模板会单独绘制 SplitView 和内容网格的细边线；只把底色设透明
        // 仍会留下轮廓，因此两个模板边框都需要显式置零。
        NavView.Resources["NavigationViewBorderThickness"] = new Thickness(0);
        NavView.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(0);
        NavView.Resources["NavigationViewMinimalContentGridBorderThickness"] = new Thickness(0);
        _videoElements = [HomeBackdropVideo, HomeBackdropVideoNext];
        _homeBackdrop.Changed += ApplyHomeBackdrop;
        _appearance.Changed += ApplyAppearance;
        WindowRoot.ActualThemeChanged += (_, _) => ApplyAppearance();
        Closed += (_, _) =>
        {
            _appearance.Changed -= ApplyAppearance;
            _homeBackdrop.Changed -= ApplyHomeBackdrop;
            PrepareHomeMediaForClose();
            ++_backgroundRequest;
        };
        ApplyAppearance();
    }

    private async void ApplyAppearance()
    {
        if (_closingHomeMedia) return;
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
        BackgroundImage.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        var options = settings.Current;
        BackgroundImage.Opacity = double.IsFinite(options.Opacity) ? Math.Clamp(options.Opacity, 0, 1) : 0.6;
        BackgroundOverlay.Opacity = double.IsFinite(options.OverlayOpacity) ? Math.Clamp(options.OverlayOpacity, 0, 1) : 0.25;
        BackgroundImage.Stretch = options.Stretch switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
        // 遮罩仅服务于用户自定义图片；首页动画播放时隐藏遮罩以保留原始亮度。
        BackgroundOverlay.Visibility = enabled && !_homeBackdropState.IsActive
            ? Visibility.Visible : Visibility.Collapsed;
        var pageBrush = (SolidColorBrush)Application.Current.Resources["AppearancePageBackground"];
        var defaultPage = (SolidColorBrush)Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"];
        var windowBackdropActive = enabled || _homeBackdropState.IsActive;
        pageBrush.Color = windowBackdropActive ? Colors.Transparent : defaultPage.Color;
        _contentBrush.Color = windowBackdropActive ? Colors.Transparent
            : ((SolidColorBrush)Application.Current.Resources["NavigationViewContentBackground"]).Color;
        // 侧边栏黑色底层与导航图标分离，透明度滑块只控制黑底，不使图标一起变淡。
        var sidebarProfile = _appearance.SidebarPreviewPageId is { } previewPage
            ? _appearance.Settings.GetEffective(previewPage) : settings;
        var sidebarOpacity = double.IsFinite(sidebarProfile.SidebarOpacity)
            ? Math.Clamp(sidebarProfile.SidebarOpacity, 0d, 1d) : 1d;
        SidebarBackdrop.Opacity = sidebarOpacity;
    }

    private void ApplyHomeBackdrop(HomeBackdropState state)
    {
        if (_closingHomeMedia) return;
        _homeBackdropState = state;
        if (!state.IsActive)
        {
            StopHomeBackdrop();
            ApplyAppearance();
            return;
        }

        if (state.ForceBlack)
        {
            StopHomeBackdrop();
            HomeBackdropBlack.Visibility = Visibility.Visible;
            ApplyAppearance();
            return;
        }

        HomeBackdropBlack.Visibility = Visibility.Collapsed;
        var videoSource = IsMediaFile(state.Background?.LocalPath, ".mp4", ".webm")
            ? state.Background?.LocalPath : state.Background?.VideoUrl;
        if (!state.PlayAnimation || !TryResolveMediaUri(videoSource, out var videoUri))
        {
            ShowHomeBackdropImage(state.Background);
            StopHomeBackdropVideo();
            ApplyAppearance();
            return;
        }

        var originalPath = videoUri.IsFile ? videoUri.LocalPath : null;
        if (originalPath is not null && HomeVideoOptimizationService.Instance.FindOptimized(originalPath) is { } optimized)
            videoUri = new Uri(optimized);

        // 远程 URL 充当动画稳定身份：下载完成后地址从 HTTPS 变为本地文件，
        // 不能因此重新启动同一段动画；优化版 MP4 则需要单独切换播放器。
        var key = state.Background?.VideoUrl ?? videoUri.AbsoluteUri;
        if (_activeVideoSlot >= 0 && _videoKeys[_activeVideoSlot] == key)
        {
            // 已经在播放同一游戏时仅检查是否出现了新的优化副本，避免元数据刷新
            // 把正在播放的动画重置到第一帧。
            if (originalPath is not null && videoUri.LocalPath != _videoSources[_activeVideoSlot]
                && videoUri.LocalPath.EndsWith(".optimized.mp4", StringComparison.OrdinalIgnoreCase))
            {
                if (_pendingVideoSlot < 0 || _videoSources[_pendingVideoSlot] != videoUri.LocalPath)
                    StageHomeVideo(videoUri, key);
            }
            else if (originalPath is not null)
                StartVideoOptimization(originalPath, key);
            else if (_pendingVideoSlot >= 0 && _videoKeys[_pendingVideoSlot] != key)
                CancelPendingVideo();
            HomeBackdropImage.Visibility = Visibility.Collapsed;
            ApplyAppearance();
            return;
        }
        if (_pendingVideoSlot >= 0 && _videoKeys[_pendingVideoSlot] == key) return;
        ShowHomeBackdropImage(state.Background);
        StageHomeVideo(videoUri, key);
        if (originalPath is not null) StartVideoOptimization(originalPath, key);
        ApplyAppearance();
    }

    private async void StartVideoOptimization(string source, string key)
    {
        // 页面刷新可能多次发布相同背景；相同源的任务保持单飞，切换游戏才取消。
        if (HomeVideoOptimizationService.Instance.FindOptimized(source) is not null) return;
        if (_videoOptimizationCancellation is { IsCancellationRequested: false }
            && _optimizingSource == source && _optimizingKey == key) return;
        _videoOptimizationCancellation?.Cancel();
        _videoOptimizationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _videoOptimizationCancellation = cancellation;
        _optimizingSource = source;
        _optimizingKey = key;
        try
        {
            var optimized = await HomeVideoOptimizationService.Instance.OptimizeAsync(source, cancellation.Token);
            if (optimized is null || cancellation.IsCancellationRequested || _closingHomeMedia) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                // 转换在后台完成，回到 UI 线程后再次核对游戏身份，防止旧游戏动画覆盖新游戏。
                if (cancellation.IsCancellationRequested || _closingHomeMedia || !_homeBackdropState.IsActive) return;
                var current = _activeVideoSlot >= 0 ? _activeVideoSlot : _pendingVideoSlot;
                if (current < 0 || _videoKeys[current] != key || _videoSources[current] == optimized) return;
                StageHomeVideo(new Uri(optimized), key);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_videoOptimizationCancellation, cancellation))
            {
                _videoOptimizationCancellation = null;
                _optimizingSource = "";
                _optimizingKey = "";
                cancellation.Dispose();
            }
        }
    }

    private void ShowHomeBackdropImage(Models.Home.HomeBackground? background)
    {
        HomeBackdropImage.Source = null;
        HomeBackdropImage.Visibility = Visibility.Collapsed;
        var candidate = IsMediaFile(background?.LocalPath, ".jpg", ".jpeg", ".png", ".webp", ".gif")
            ? background?.LocalPath : background?.ImageUrl;
        if (!TryResolveMediaUri(candidate, out var imageUri)) return;
        try
        {
            HomeBackdropImage.Source = new BitmapImage(imageUri);
            HomeBackdropImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 静态背景无法显示：{ex.Message}");
        }
    }

    private void StageHomeVideo(Uri uri, string key)
    {
        // 双播放器槽位：新视频先在透明层打开，首帧可用后再淡入并释放旧播放器。
        if (_activeVideoSlot >= 0 && _videoKeys[_activeVideoSlot] != key)
        {
            _videoOptimizationCancellation?.Cancel();
            _videoOptimizationCancellation?.Dispose();
            _videoOptimizationCancellation = null;
            _optimizingSource = "";
            _optimizingKey = "";
        }
        CancelPendingVideo();
        FinishVideoTransition();
        var slot = _activeVideoSlot == 0 ? 1 : 0;
        var request = ++_videoRequest;
        _pendingVideoSlot = slot;
        _videoKeys[slot] = key;
        _videoSources[slot] = uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
        var element = _videoElements[slot];
        try
        {
            var usePlaylist = uri.IsFile && Path.GetExtension(uri.LocalPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            var player = new MediaPlayer { IsLoopingEnabled = !usePlaylist, AutoPlay = true };
            _videoPlayers[slot] = player;
            player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
                OnPendingVideoOpened(slot, request, player));
            player.MediaFailed += (_, args) =>
            {
                var message = args.ErrorMessage;
                DispatcherQueue.TryEnqueue(() => OnHomeVideoFailed(slot, request, player, message));
            };
            element.Opacity = 0;
            // Collapsed 的 MediaPlayerElement 可能不会创建渲染表面，导致 MediaOpened
            // 永远不触发；预备播放器必须保持 Visible、Opacity=0。
            element.Visibility = Visibility.Visible;
            Canvas.SetZIndex(element, 2);
            if (_activeVideoSlot >= 0) Canvas.SetZIndex(_videoElements[_activeVideoSlot], 1);
            element.SetMediaPlayer(player);
            if (usePlaylist)
            {
                // 本地 MP4 放入两个相同的播放项，让下一个播放项提前预读。
                // 单媒体源循环需要在片尾 seek 回起始关键帧，容易短暂停在末帧。
                var playlist = new MediaPlaybackList
                {
                    AutoRepeatEnabled = true,
                    MaxPrefetchTime = TimeSpan.FromMilliseconds(800),
                };
                playlist.Items.Add(new MediaPlaybackItem(MediaSource.CreateFromUri(uri)));
                playlist.Items.Add(new MediaPlaybackItem(MediaSource.CreateFromUri(uri)));
                player.Source = playlist;
            }
            else
            {
                player.Source = MediaSource.CreateFromUri(uri);
            }
            player.Play();
        }
        catch (Exception ex)
        {
            DisposeVideoSlot(slot);
            _pendingVideoSlot = -1;
            if (_activeVideoSlot >= 0)
            {
                DisposeVideoSlot(_activeVideoSlot);
                _activeVideoSlot = -1;
            }
            ShowHomeBackdropImage(_homeBackdropState.Background);
            LogService.Instance.AddLog($"[首页背景] 动画无法播放：{ex.Message}");
        }
    }

    private void OnPendingVideoOpened(int slot, int request, MediaPlayer player)
    {
        if (_closingHomeMedia || request != _videoRequest || _pendingVideoSlot != slot
            || !ReferenceEquals(_videoPlayers[slot], player)) return;
        _pendingVideoTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pendingVideoTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_pendingVideoTimer, timer)) _pendingVideoTimer = null;
            if (_closingHomeMedia || request != _videoRequest || _pendingVideoSlot != slot
                || !ReferenceEquals(_videoPlayers[slot], player)) return;
            ActivatePendingVideo(slot, request);
        };
        timer.Start();
    }

    private void ActivatePendingVideo(int slot, int request)
    {
        var previous = _activeVideoSlot;
        _activeVideoSlot = slot;
        _pendingVideoSlot = -1;
        var element = _videoElements[slot];
        element.Visibility = Visibility.Visible;
        HomeBackdropImage.Visibility = Visibility.Collapsed;
        var fade = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(280))
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var transition = new Storyboard();
        transition.Children.Add(fade);
        _videoTransition = transition;
        transition.Completed += (_, _) =>
        {
            if (request != _videoRequest || _closingHomeMedia) return;
            element.Opacity = 1;
            _videoTransition = null;
            if (previous >= 0 && previous != slot) DisposeVideoSlot(previous);
        };
        transition.Begin();
    }

    private void OnHomeVideoFailed(int slot, int request, MediaPlayer player, string message)
    {
        if (_closingHomeMedia || request != _videoRequest
            || !ReferenceEquals(_videoPlayers[slot], player)) return;
        if (_pendingVideoSlot == slot)
        {
            _pendingVideoTimer?.Stop();
            _pendingVideoTimer = null;
            _pendingVideoSlot = -1;
            DisposeVideoSlot(slot);
            if (_activeVideoSlot >= 0)
            {
                DisposeVideoSlot(_activeVideoSlot);
                _activeVideoSlot = -1;
            }
            ShowHomeBackdropImage(_homeBackdropState.Background);
            LogService.Instance.AddLog($"[首页背景] 动画播放失败，已回退到静态背景：{message}");
            return;
        }
        if (_activeVideoSlot == slot)
        {
            _videoTransition?.Stop();
            _videoTransition = null;
            var previous = 1 - slot;
            _activeVideoSlot = _videoPlayers[previous] is null ? -1 : previous;
            if (_activeVideoSlot >= 0) _videoElements[previous].Opacity = 1;
        }
        DisposeVideoSlot(slot);
        if (_activeVideoSlot < 0) ShowHomeBackdropImage(_homeBackdropState.Background);
        LogService.Instance.AddLog($"[首页背景] 动画播放失败，已回退到静态背景：{message}");
    }

    private void CancelPendingVideo()
    {
        _pendingVideoTimer?.Stop();
        _pendingVideoTimer = null;
        if (_pendingVideoSlot < 0) return;
        var slot = _pendingVideoSlot;
        _pendingVideoSlot = -1;
        ++_videoRequest;
        DisposeVideoSlot(slot);
    }

    private void FinishVideoTransition()
    {
        if (_videoTransition is null) return;
        _videoTransition.Stop();
        _videoTransition = null;
        if (_activeVideoSlot < 0) return;
        _videoElements[_activeVideoSlot].Opacity = 1;
        DisposeVideoSlot(1 - _activeVideoSlot);
    }

    private void DisposeVideoSlot(int slot)
    {
        var element = _videoElements[slot];
        var player = _videoPlayers[slot];
        _videoPlayers[slot] = null;
        _videoKeys[slot] = "";
        _videoSources[slot] = "";
        element.Visibility = Visibility.Collapsed;
        element.Opacity = 0;
        try { element.SetMediaPlayer(null); }
        catch (Exception ex) { LogService.Instance.AddLog($"[首页背景] 解除播放器绑定失败：{ex.Message}"); }
        if (player is null) return;
        try { player.Source = null; }
        catch (Exception ex) { LogService.Instance.AddLog($"[首页背景] 清空视频源失败：{ex.Message}"); }
        try { player.Dispose(); }
        catch (Exception ex) { LogService.Instance.AddLog($"[首页背景] 释放播放器失败：{ex.Message}"); }
    }

    private static bool TryResolveMediaUri(string? source, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (Path.IsPathFullyQualified(source))
        {
            if (!File.Exists(source)) return false;
            uri = new Uri(source, UriKind.Absolute);
            return true;
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var parsed)) return false;
        if (parsed.IsFile && !File.Exists(parsed.LocalPath)) return false;
        if (!parsed.IsFile && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        uri = parsed;
        return true;
    }

    private static bool IsMediaFile(string? source, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath : source;
        return extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private void StopHomeBackdropVideo()
    {
        _videoOptimizationCancellation?.Cancel();
        _videoOptimizationCancellation?.Dispose();
        _videoOptimizationCancellation = null;
        _optimizingSource = "";
        _optimizingKey = "";
        ++_videoRequest;
        CancelPendingVideo();
        FinishVideoTransition();
        if (_activeVideoSlot >= 0)
        {
            DisposeVideoSlot(_activeVideoSlot);
            _activeVideoSlot = -1;
        }
    }

    private void StopHomeBackdrop()
    {
        StopHomeBackdropVideo();
        HomeBackdropImage.Source = null;
        HomeBackdropImage.Visibility = Visibility.Collapsed;
        HomeBackdropBlack.Visibility = Visibility.Collapsed;
    }

    private void PrepareHomeMediaForClose()
    {
        if (_closingHomeMedia) return;
        _closingHomeMedia = true;
        ++_backgroundRequest;
        _homeBackdrop.Changed -= ApplyHomeBackdrop;
        try { StopHomeBackdrop(); }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 关闭背景失败：{ex.Message}");
        }
        // SetMediaPlayer(null) happens in DisposeVideoSlot before the WinUI visual
        // tree is destroyed; disposing an attached player can leave a stale frame.
        foreach (var slot in Enumerable.Range(0, _videoElements.Length))
        {
            if (_videoPlayers[slot] is not null) DisposeVideoSlot(slot);
        }
    }
}
