using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher;

public sealed partial class MainWindow
{
    private static readonly bool UseOriginalHomeVideoForDiagnostics =
        Environment.GetEnvironmentVariable("STEAMCN_HOME_VIDEO_ORIGINAL") == "1";
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
    private readonly bool[] _videoReady = new bool[2];
    private readonly bool[] _videoFrameReady = new bool[2];
    private readonly int[] _videoFrameCopyQueued = new int[2];
    private readonly TimeSpan[] _videoLastFramePositions = [TimeSpan.Zero, TimeSpan.Zero];
    private HomeVideoFrameRenderer? _videoFrameRenderer;
    private int _activeVideoSlot = -1;
    private int _pendingVideoSlot = -1;
    private int _transitionOldSlot = -1;
    private DispatcherTimer? _videoTransitionTimer;
    private DateTimeOffset _videoTransitionStartedAt;
    private float _videoTransitionProgress;
    private DispatcherTimer? _videoTransitionCleanupTimer;
    private DispatcherTimer? _videoLoopTransitionTimer;
    private DateTimeOffset _videoLoopTransitionStartedAt;
    private float _videoLoopTransitionProgress;
    private CancellationTokenSource? _videoOptimizationCancellation;
    private string _optimizingSource = "";
    private string _optimizingKey = "";
    private HomeBackdropState _homeBackdropState = HomeBackdropState.Inactive;
    private int _videoRequest;
    private DispatcherTimer? _pendingVideoTimer;
    private HomeVideoReadinessGate? _pendingVideoReadiness;
    private bool _closingHomeMedia;
    private bool _homeMediaSuspendedForTray;
    private DispatcherTimer? _trayResumeTimer;

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
        _videoFrameRenderer = new HomeVideoFrameRenderer(HomeBackdropVideoFrame);
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

        if (state.IsHolding)
        {
            // 游戏选择已变化，但新来源尚未返回；旧动画继续播放，避免加载期间看起来卡死。
            CancelPendingVideo();
            FinishVideoTransition();
            if (_activeVideoSlot >= 0 && !_homeMediaSuspendedForTray)
                _videoPlayers[_activeVideoSlot]?.Play();
            return;
        }

        // 视频与海报之下始终保留黑底，媒体源切换时不会露出窗口主题的白色底色。
        HomeBackdropBlack.Visibility = Visibility.Visible;
        var videoSource = IsMediaFile(state.Background?.LocalPath, ".mp4", ".webm")
            ? state.Background?.LocalPath : state.Background?.VideoUrl;
        if (!state.PlayAnimation || !TryResolveMediaUri(videoSource, out var videoUri))
        {
            StopHomeBackdropVideo();
            ShowHomeBackdropImage(state.Background);
            // 未适配或没有可用媒体时，让底层外观图片露出。
            HomeBackdropBlack.Visibility = HomeBackdropImage.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyAppearance();
            return;
        }

        var originalPath = videoUri.IsFile ? videoUri.LocalPath : null;
        // 远程 URL 充当动画稳定身份：下载完成后地址从 HTTPS 变为本地文件，
        // 不能因此重新启动同一段动画；优化版 MP4 可以更换同一播放器的媒体源。
        var key = state.Background?.VideoUrl ?? videoUri.AbsoluteUri;
        if (!videoUri.IsFile)
        {
            // 媒体缓存完成前只显示当前版本海报。远程 MP4 尚未经过兼容性检测，
            // 直接交给 Media Foundation 可能在原生解码/合成层导致整个进程崩溃。
            StopHomeBackdropVideo();
            ShowHomeBackdropImage(state.Background);
            HomeBackdropBlack.Visibility = HomeBackdropImage.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyAppearance();
            return;
        }

        if (!UseOriginalHomeVideoForDiagnostics && originalPath is not null)
        {
            if (HomeVideoOptimizationService.Instance.FindOptimized(originalPath) is { } optimized)
            {
                videoUri = new Uri(optimized);
            }
            else if (Path.GetExtension(originalPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                && !HomeVideoOptimizationService.Instance.IsKnownCompatibleMp4(originalPath))
            {
                PrepareHomeMp4Async(originalPath, key);
                ApplyAppearance();
                return;
            }
        }

        var source = videoUri.IsFile ? videoUri.LocalPath : videoUri.AbsoluteUri;
        if ((_activeVideoSlot >= 0 && _videoKeys[_activeVideoSlot] == key && _videoSources[_activeVideoSlot] == source)
            || (_pendingVideoSlot >= 0 && _videoKeys[_pendingVideoSlot] == key && _videoSources[_pendingVideoSlot] == source))
        {
            if (_pendingVideoSlot < 0 && _activeVideoSlot >= 0 && !_homeMediaSuspendedForTray)
                _videoPlayers[_activeVideoSlot]?.Play();
            if (!UseOriginalHomeVideoForDiagnostics && originalPath is not null
                && Path.GetExtension(originalPath).Equals(".webm", StringComparison.OrdinalIgnoreCase))
                StartVideoOptimization(originalPath, key);
            ApplyAppearance();
            return;
        }
        if (_activeVideoSlot < 0) ShowHomeVideoCover(state.Background);
        else HideHomeVideoCover();
        StageHomeVideo(videoUri, key);
        if (!UseOriginalHomeVideoForDiagnostics && originalPath is not null
            && Path.GetExtension(originalPath).Equals(".webm", StringComparison.OrdinalIgnoreCase))
            StartVideoOptimization(originalPath, key);
        ApplyAppearance();
    }

    private async void PrepareHomeMp4Async(string source, string key)
    {
        if (_videoOptimizationCancellation is { IsCancellationRequested: false }
            && _optimizingSource == source && _optimizingKey == key) return;
        _videoOptimizationCancellation?.Cancel();
        _videoOptimizationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _videoOptimizationCancellation = cancellation;
        _optimizingSource = source;
        _optimizingKey = key;

        // 兼容性转换期间保留并继续播放旧动画；首次进入首页时才用当前游戏海报兜底。
        CancelPendingVideo();
        FinishVideoTransition();
        if (_activeVideoSlot < 0)
            ShowHomeVideoCover(_homeBackdropState.Background);
        try
        {
            var optimized = await HomeVideoOptimizationService.Instance.OptimizeAsync(source, cancellation.Token);
            if (cancellation.IsCancellationRequested || _closingHomeMedia) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (cancellation.IsCancellationRequested || _closingHomeMedia || !_homeBackdropState.IsActive
                    || _homeBackdropState.Background?.VideoUrl != key) return;
                var playable = optimized ?? source;
                if (optimized is not null)
                    LogService.Instance.AddLog("[首页背景] 兼容动画已生成，正在启动播放器");
                StageHomeVideo(new Uri(playable), key);
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
            if (cancellation.IsCancellationRequested || _closingHomeMedia) return;
            if (optimized is null) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (cancellation.IsCancellationRequested || _closingHomeMedia || !_homeBackdropState.IsActive) return;
                var slot = _pendingVideoSlot >= 0 ? _pendingVideoSlot : _activeVideoSlot;
                if (slot >= 0)
                {
                    if (_videoKeys[slot] != key) return;
                }
                else if (_homeBackdropState.Background?.VideoUrl != key)
                {
                    return;
                }
                // 不在当前播放中途二次切源；优化副本由下一次进入该游戏时直接命中。
                LogService.Instance.AddLog("[首页背景] 已生成兼容动画副本，下次进入时使用");
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

    private void ShowHomeVideoCover(Models.Home.HomeBackground? background)
    {
        HomeVideoLoadingBlack.Visibility = Visibility.Visible;
        HomeVideoLoadingImage.Source = null;
        HomeVideoLoadingImage.Visibility = Visibility.Collapsed;
        var candidate = IsMediaFile(background?.LocalPath, ".jpg", ".jpeg", ".png", ".webp", ".gif")
            ? background?.LocalPath : background?.ImageUrl;
        if (!TryResolveMediaUri(candidate, out var imageUri)) return;
        try
        {
            HomeVideoLoadingImage.Source = new BitmapImage(imageUri);
            HomeVideoLoadingImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 切换海报无法显示：{ex.Message}");
        }
    }

    private void HideHomeVideoCover()
    {
        HomeVideoLoadingImage.Visibility = Visibility.Collapsed;
        HomeVideoLoadingImage.Source = null;
        HomeVideoLoadingBlack.Visibility = Visibility.Collapsed;
    }

    private void StageHomeVideo(Uri uri, string key)
    {
        var previousKey = _pendingVideoSlot >= 0 ? _videoKeys[_pendingVideoSlot]
            : _activeVideoSlot >= 0 ? _videoKeys[_activeVideoSlot] : "";
        if (previousKey != key && _optimizingKey != key)
        {
            _videoOptimizationCancellation?.Cancel();
            _videoOptimizationCancellation?.Dispose();
            _videoOptimizationCancellation = null;
            _optimizingSource = "";
            _optimizingKey = "";
        }
        FinishVideoTransition();
        CancelPendingVideo();
        var oldSlot = _activeVideoSlot;
        var slot = oldSlot == 0 ? 1 : 0;
        var request = ++_videoRequest;
        _pendingVideoSlot = slot;
        _videoKeys[slot] = key;
        _videoSources[slot] = uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
        _videoReady[slot] = false;
        _videoFrameReady[slot] = false;
        _videoLastFramePositions[slot] = TimeSpan.Zero;
        _pendingVideoReadiness = new HomeVideoReadinessGate();
        if (UseOriginalHomeVideoForDiagnostics && uri.IsFile &&
            Path.GetExtension(uri.LocalPath).Equals(".webm", StringComparison.OrdinalIgnoreCase))
            LogService.Instance.AddLog("[首页背景] 诊断模式：播放原始 WebM 视频");
        LogService.Instance.AddLog($"[首页背景] 准备动画：{(uri.IsFile ? Path.GetFileName(uri.LocalPath) : "远程视频")}，请求={request}");
        try
        {
            var usePlaylist = uri.IsFile && Path.GetExtension(uri.LocalPath)
                .Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            var player = new MediaPlayer
            {
                IsLoopingEnabled = !usePlaylist,
                IsVideoFrameServerEnabled = true,
                AutoPlay = false,
            };
            _videoPlayers[slot] = player;
            player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => OnHomeVideoOpened(player, slot, request));
            player.VideoFrameAvailable += (_, _) => QueueHomeVideoFrame(player, slot, request);
            player.MediaFailed += (_, args) =>
            {
                var message = args.ErrorMessage;
                DispatcherQueue.TryEnqueue(() => OnHomeVideoFailed(player, slot, request, message));
            };
            player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
                OnHomeVideoFailed(player, slot, request, "动画意外结束，未能继续循环播放。"));
            if (usePlaylist)
            {
                // 两个相同播放项让本地 MP4 的下一轮提前预读，避免片尾重新 seek 的停顿。
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
            if (!_homeMediaSuspendedForTray) player.Play();
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 动画无法播放：{ex.Message}");
            CancelPendingVideo();
            if (oldSlot >= 0 && !_homeMediaSuspendedForTray) _videoPlayers[oldSlot]?.Play();
        }
    }

    private void QueueHomeVideoFrame(MediaPlayer player, int slot, int request)
    {
        if (Interlocked.Exchange(ref _videoFrameCopyQueued[slot], 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => CopyAndPresentHomeVideoFrame(player, slot, request)))
            Interlocked.Exchange(ref _videoFrameCopyQueued[slot], 0);
    }

    private void CopyAndPresentHomeVideoFrame(MediaPlayer player, int slot, int request)
    {
        try
        {
            if (_closingHomeMedia || request != _videoRequest
                || !ReferenceEquals(_videoPlayers[slot], player)) return;
            var session = player.PlaybackSession;
            var width = session.NaturalVideoWidth;
            var height = session.NaturalVideoHeight;
            if (width == 0 || height == 0 || _videoFrameRenderer is null) return;

            var position = session.Position;
            var previousPosition = _videoLastFramePositions[slot];
            var wrapped = slot == _activeVideoSlot
                && _transitionOldSlot < 0
                && previousPosition >= TimeSpan.FromSeconds(1)
                && position + TimeSpan.FromMilliseconds(500) < previousPosition;
            if (wrapped && _videoFrameRenderer.CaptureLoopHold(slot))
                BeginLoopFrameTransition();

            if (!_videoFrameRenderer.CopyFrame(player, slot, width, height)) return;
            _videoFrameReady[slot] = true;
            _videoLastFramePositions[slot] = position;
            PresentCurrentHomeVideoFrame();
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 视频帧复制失败：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _videoFrameCopyQueued[slot], 0);
        }
    }

    private void PresentCurrentHomeVideoFrame()
    {
        if (_videoFrameRenderer is null || _activeVideoSlot < 0) return;
        if (_transitionOldSlot >= 0)
            _videoFrameRenderer.RenderTransition(
                _transitionOldSlot, _activeVideoSlot, _videoTransitionProgress);
        else if (_videoLoopTransitionTimer is not null)
            _videoFrameRenderer.RenderLoopTransition(_activeVideoSlot, _videoLoopTransitionProgress);
        else
            _videoFrameRenderer.RenderSingle(_activeVideoSlot);
    }

    private void BeginLoopFrameTransition()
    {
        _videoLoopTransitionTimer?.Stop();
        _videoLoopTransitionProgress = 0;
        _videoLoopTransitionStartedAt = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _videoLoopTransitionTimer = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_videoLoopTransitionTimer, timer)) return;
            var elapsed = (DateTimeOffset.UtcNow - _videoLoopTransitionStartedAt).TotalMilliseconds;
            _videoLoopTransitionProgress = SmoothStep((float)(elapsed / 300d));
            PresentCurrentHomeVideoFrame();
            if (_videoLoopTransitionProgress < 1) return;
            timer.Stop();
            _videoLoopTransitionTimer = null;
            _videoLoopTransitionProgress = 0;
            PresentCurrentHomeVideoFrame();
        };
        timer.Start();
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private void OnHomeVideoOpened(MediaPlayer player, int slot, int request)
    {
        if (_closingHomeMedia || _pendingVideoSlot != slot || request != _videoRequest
            || !ReferenceEquals(_videoPlayers[slot], player)) return;
        _videoReady[slot] = true;
        LogService.Instance.AddLog($"[首页背景] 动画已打开：请求={request}");
        if (_homeMediaSuspendedForTray) return;
        _pendingVideoTimer?.Stop();
        var readiness = _pendingVideoReadiness ??= new HomeVideoReadinessGate();
        readiness.Start(DateTimeOffset.UtcNow);
        // MediaOpened 只说明媒体已解析。轮询真实播放进度和自然尺寸，让视频表面先在
        // 旧动画/海报下方完成首帧解码与 UniformToFill 布局，再进行可见切换。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pendingVideoTimer = timer;
        timer.Tick += (_, _) =>
        {
            if (_closingHomeMedia || request != _videoRequest || _pendingVideoSlot != slot
                || !ReferenceEquals(_videoPlayers[slot], player))
            {
                timer.Stop();
                if (ReferenceEquals(_pendingVideoTimer, timer)) _pendingVideoTimer = null;
                return;
            }
            var session = player.PlaybackSession;
            var status = readiness.Observe(
                DateTimeOffset.UtcNow,
                session.PlaybackState == MediaPlaybackState.Playing,
                session.Position,
                session.NaturalVideoWidth,
                session.NaturalVideoHeight,
                _videoFrameReady[slot]);
            if (status == HomeVideoReadinessStatus.Waiting) return;
            timer.Stop();
            if (ReferenceEquals(_pendingVideoTimer, timer)) _pendingVideoTimer = null;
            if (status == HomeVideoReadinessStatus.Ready)
                ActivatePendingVideo(slot, request);
            else
                OnHomeVideoFailed(player, slot, request, "首帧在限定时间内未稳定，已使用静态背景。");
        };
        timer.Start();
    }

    private void ActivatePendingVideo(int slot, int request)
    {
        if (_pendingVideoSlot != slot || request != _videoRequest) return;
        var oldSlot = _activeVideoSlot;
        _pendingVideoSlot = -1;
        _pendingVideoReadiness = null;
        _activeVideoSlot = slot;
        HomeBackdropImage.Visibility = Visibility.Collapsed;
        HideHomeVideoCover();
        HomeBackdropVideoFrame.Visibility = Visibility.Visible;
        if (oldSlot < 0)
        {
            _videoFrameRenderer?.RenderSingle(slot);
            LogService.Instance.AddLog($"[首页背景] 动画已显示：请求={request}");
            return;
        }

        // 两路解码帧都已进入 Win2D 纹理。界面始终只显示同一个 ImageSource，
        // 切换只在固定输出纹理内混合，不再交接两个原生视频表面。
        _videoLoopTransitionTimer?.Stop();
        _videoLoopTransitionTimer = null;
        _videoPlayers[oldSlot]?.Pause();
        _transitionOldSlot = oldSlot;
        _videoTransitionProgress = 0;
        _videoTransitionStartedAt = DateTimeOffset.UtcNow;
        var transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _videoTransitionTimer = transitionTimer;
        transitionTimer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_videoTransitionTimer, transitionTimer)) return;
            var elapsed = (DateTimeOffset.UtcNow - _videoTransitionStartedAt).TotalMilliseconds;
            _videoTransitionProgress = SmoothStep((float)(elapsed / 500d));
            PresentCurrentHomeVideoFrame();
            if (_videoTransitionProgress < 1) return;
            transitionTimer.Stop();
            _videoTransitionTimer = null;
            // 让最后一帧先提交到固定输出纹理，再释放旧解码器。
            var cleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _videoTransitionCleanupTimer = cleanupTimer;
            cleanupTimer.Tick += (_, _) =>
            {
                cleanupTimer.Stop();
                if (!ReferenceEquals(_videoTransitionCleanupTimer, cleanupTimer)) return;
                _videoTransitionCleanupTimer = null;
                FinishVideoTransition();
            };
            cleanupTimer.Start();
        };
        PresentCurrentHomeVideoFrame();
        transitionTimer.Start();
        LogService.Instance.AddLog($"[首页背景] 动画已显示：请求={request}");
    }

    private void OnHomeVideoFailed(MediaPlayer player, int slot, int request, string message)
    {
        if (_closingHomeMedia || request != _videoRequest
            || !ReferenceEquals(_videoPlayers[slot], player)) return;
        var isPending = _pendingVideoSlot == slot;
        var isActive = _activeVideoSlot == slot;
        if (!isPending && !isActive) return;
        var sameGame = isPending && _activeVideoSlot >= 0
            && _videoKeys[_activeVideoSlot] == _videoKeys[slot];
        if (isPending) CancelPendingVideo();
        if (sameGame && _activeVideoSlot >= 0 && !_homeMediaSuspendedForTray)
        {
            _videoPlayers[_activeVideoSlot]?.Play();
        }
        else
        {
            // 新动画在 MediaOpened/展示之后仍可能因编码不兼容而失败；无论失败发生在
            // 预热还是已显示阶段，都不能把黑色视频表面永久留在窗口上。
            StopHomeBackdropVideo(cancelOptimization: false);
            ShowHomeBackdropImage(_homeBackdropState.Background);
            HomeBackdropBlack.Visibility = HomeBackdropImage.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyAppearance();
        }
        LogService.Instance.AddLog($"[首页背景] 动画播放失败，已使用可用背景：{message}");
    }

    private void FinishVideoTransition()
    {
        _videoTransitionCleanupTimer?.Stop();
        _videoTransitionCleanupTimer = null;
        _videoTransitionTimer?.Stop();
        _videoTransitionTimer = null;
        _videoTransitionProgress = 0;
        if (_transitionOldSlot >= 0)
        {
            DisposeVideoSlot(_transitionOldSlot);
            _transitionOldSlot = -1;
        }
        if (_activeVideoSlot >= 0)
            _videoFrameRenderer?.RenderSingle(_activeVideoSlot);
    }

    private void CancelPendingVideo()
    {
        ++_videoRequest;
        _pendingVideoTimer?.Stop();
        _pendingVideoTimer = null;
        _pendingVideoReadiness = null;
        if (_pendingVideoSlot < 0) return;
        DisposeVideoSlot(_pendingVideoSlot);
        _pendingVideoSlot = -1;
    }

    private void DisposeVideoSlot(int slot)
    {
        var player = _videoPlayers[slot];
        _videoPlayers[slot] = null;
        _videoKeys[slot] = "";
        _videoSources[slot] = "";
        _videoReady[slot] = false;
        _videoFrameReady[slot] = false;
        _videoLastFramePositions[slot] = TimeSpan.Zero;
        Interlocked.Exchange(ref _videoFrameCopyQueued[slot], 0);
        _videoFrameRenderer?.ClearSlot(slot);
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

    private void StopHomeBackdropVideo(bool cancelOptimization = true, bool hideCover = true)
    {
        if (cancelOptimization)
        {
            _videoOptimizationCancellation?.Cancel();
            _videoOptimizationCancellation?.Dispose();
            _videoOptimizationCancellation = null;
            _optimizingSource = "";
            _optimizingKey = "";
        }
        CancelPendingVideo();
        FinishVideoTransition();
        if (_activeVideoSlot >= 0)
        {
            DisposeVideoSlot(_activeVideoSlot);
            _activeVideoSlot = -1;
        }
        _videoLoopTransitionTimer?.Stop();
        _videoLoopTransitionTimer = null;
        _videoLoopTransitionProgress = 0;
        HomeBackdropVideoFrame.Visibility = Visibility.Collapsed;
        _videoFrameRenderer?.ClearOutput();
        if (hideCover) HideHomeVideoCover();
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
        _trayResumeTimer?.Stop();
        _trayResumeTimer = null;
        ++_backgroundRequest;
        _homeBackdrop.Changed -= ApplyHomeBackdrop;
        try { StopHomeBackdrop(); }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[首页背景] 关闭背景失败：{ex.Message}");
        }
        _videoFrameRenderer?.Dispose();
        _videoFrameRenderer = null;
        HomeBackdropVideoFrame.Source = null;
        // StopHomeBackdrop 已在 XAML 树销毁前释放两个解码器与固定输出纹理。
    }

    private void SuspendHomeMediaForTray()
    {
        _trayResumeTimer?.Stop();
        _trayResumeTimer = null;
        if (_homeMediaSuspendedForTray || _closingHomeMedia) return;
        _homeMediaSuspendedForTray = true;
        _pendingVideoTimer?.Stop();
        _pendingVideoTimer = null;
        // 隐藏期间持续播放可能使视频表面与解码时钟失去同步。
        // 保留播放器及当前位置，仅暂停解码，恢复时不用重新打开媒体源。
        try
        {
            FinishVideoTransition();
            if (_activeVideoSlot >= 0) _videoPlayers[_activeVideoSlot]?.Pause();
            if (_pendingVideoSlot >= 0) _videoPlayers[_pendingVideoSlot]?.Pause();
        }
        catch (Exception ex) { LogService.Instance.AddLog($"[首页背景] 托盘暂停动画失败：{ex.Message}"); }
    }

    private void ResumeHomeMediaFromTray()
    {
        if (!_homeMediaSuspendedForTray || _closingHomeMedia) return;
        _trayResumeTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _trayResumeTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_trayResumeTimer, timer)) return;
            _trayResumeTimer = null;
            if (_closingHomeMedia) return;
            _homeMediaSuspendedForTray = false;
            try
            {
                if (_activeVideoSlot >= 0)
                    _videoPlayers[_activeVideoSlot]?.Play();
                if (_pendingVideoSlot >= 0)
                    _videoPlayers[_pendingVideoSlot]?.Play();
            }
            catch (Exception ex) { LogService.Instance.AddLog($"[首页背景] 托盘恢复动画失败：{ex.Message}"); }
            if (_pendingVideoSlot >= 0 && _videoReady[_pendingVideoSlot]
                && _videoPlayers[_pendingVideoSlot] is { } pending)
                OnHomeVideoOpened(pending, _pendingVideoSlot, _videoRequest);
        };
        timer.Start();
    }
}
