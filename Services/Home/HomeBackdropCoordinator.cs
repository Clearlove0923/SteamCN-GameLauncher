using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// 首页页面只发布想展示的统一背景数据，MainWindow 统一拥有播放器和窗口底层渲染。
/// 这样页面导航和游戏切换不会销毁窗口背景层。
/// </summary>
public sealed class HomeBackdropCoordinator
{
    public static HomeBackdropCoordinator Instance { get; } = new();

    public HomeBackdropState Current { get; private set; } = HomeBackdropState.Inactive;
    public event Action<HomeBackdropState>? Changed;
    private Guid? _owner;

    private HomeBackdropCoordinator() { }

    public void Show(Guid owner, HomeBackground? background, bool playAnimation, bool forceBlack = false)
    {
        _owner = owner;
        Current = new HomeBackdropState(true, playAnimation, forceBlack, background);
        Changed?.Invoke(Current);
    }

    public void Hold(Guid owner)
    {
        if (_owner != owner || !Current.IsActive) return;
        Current = Current with { IsHolding = true };
        Changed?.Invoke(Current);
    }

    public void Clear(Guid owner)
    {
        // 新页面可能先完成加载，旧页面稍后才 Unloaded；只有当前 owner 能清空背景，
        // 防止旧页面的迟到回调抹掉新游戏动画。
        if (_owner != owner) return;
        _owner = null;
        Current = HomeBackdropState.Inactive;
        Changed?.Invoke(Current);
    }
}

public sealed record HomeBackdropState(
    bool IsActive,
    bool PlayAnimation,
    bool ForceBlack,
    HomeBackground? Background,
    bool IsHolding = false)
{
    public static HomeBackdropState Inactive { get; } = new(false, false, false, null);
}
