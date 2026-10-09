namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// Tracks whether a prewarmed home video has produced a stable, advancing frame stream.
/// MediaOpened alone only confirms that the source was parsed; exposing the surface at that
/// point does not guarantee that the frame server has copied a renderable video frame.
/// </summary>
public sealed class HomeVideoReadinessGate
{
    public static readonly TimeSpan MinimumPlaybackPosition = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan MaximumWarmup = TimeSpan.FromSeconds(4);
    public const int RequiredStableSamples = 5;

    private DateTimeOffset _startedAt;
    private TimeSpan _lastPosition;
    private uint _lastWidth;
    private uint _lastHeight;
    private int _stableSamples;

    public void Start(DateTimeOffset now)
    {
        _startedAt = now;
        _lastPosition = TimeSpan.Zero;
        _lastWidth = 0;
        _lastHeight = 0;
        _stableSamples = 0;
    }

    public HomeVideoReadinessStatus Observe(
        DateTimeOffset now,
        bool isPlaying,
        TimeSpan position,
        uint naturalWidth,
        uint naturalHeight,
        bool hasDecodedFrame = true)
    {
        if (isPlaying && hasDecodedFrame && naturalWidth > 0 && naturalHeight > 0
            && position >= MinimumPlaybackPosition)
        {
            var dimensionsStable = naturalWidth == _lastWidth && naturalHeight == _lastHeight;
            var positionAdvanced = position > _lastPosition;
            _stableSamples = dimensionsStable && positionAdvanced ? _stableSamples + 1 : 1;
            _lastWidth = naturalWidth;
            _lastHeight = naturalHeight;
            _lastPosition = position;
            if (_stableSamples >= RequiredStableSamples)
                return HomeVideoReadinessStatus.Ready;
        }
        else
        {
            _stableSamples = 0;
            _lastWidth = naturalWidth;
            _lastHeight = naturalHeight;
            _lastPosition = position;
        }

        return now - _startedAt >= MaximumWarmup
            ? HomeVideoReadinessStatus.TimedOut
            : HomeVideoReadinessStatus.Waiting;
    }
}

public enum HomeVideoReadinessStatus
{
    Waiting,
    Ready,
    TimedOut,
}
