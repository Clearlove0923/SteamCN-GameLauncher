using Velopack;
using Velopack.Sources;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>
/// Velopack 自动更新服务：从 GitHub Releases 拉版本清单、下载新版本包、重启应用。
///
/// 定位：与 <see cref="UpdateService"/> 并存不冲突。
/// - <see cref="UpdateService"/>：启动期轻量检测 GitHub Release API，弹角标/强制更新遮罩。
///   不下载安装。Debug 模式下走本地 version.json。
/// - 本服务：用户点"下载并重启"按钮后触发，后台下载完整包/差分包，重启由 Velopack Update.exe
///   子进程负责原子替换。安装路径 %LocalAppData%\Programs\io.steamcn.launcher。
///
/// 未通过 Velopack 打包的运行（如 Debug 直起）会跳过本服务，不抛异常。
/// </summary>
public sealed class VelopackUpdateService
{
    public static VelopackUpdateService Instance { get; } = new();
    private VelopackUpdateService() { }

    /// <summary>
    /// Velopack 包标识，<b>首次发布后不可修改</b>（改了用户会被当成另一款软件）。
    /// 必须与 Publish-Release.ps1 中 <c>vpk pack --packId</c> 一致。
    /// </summary>
    public const string PackId = "io.steamcn.launcher";

    /// <summary>
    /// GitHub Releases 仓库地址。公开仓库无需 token。
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/Clearlove0923/SteamCN-GameLauncher";

    private static readonly GithubSource _source = new(GitHubRepoUrl, accessToken: null, prerelease: false);

    private UpdateManager? _manager;

    /// <summary>当前是否有可用更新（已检查过且未应用）。</summary>
    public bool HasPendingUpdate { get; private set; }

    /// <summary>待更新版本号（如 "3.1.0"）。</summary>
    public string? PendingVersion { get; private set; }

    /// <summary>待更新版本的发布说明（Markdown 或纯文本）。</summary>
    public string? PendingReleaseNotes { get; private set; }

    /// <summary>发现新版本时触发。参数：版本号、发布说明。</summary>
    public event Action<string, string>? UpdateAvailable;

    /// <summary>下载进度回调，0-100。</summary>
    public event Action<int>? DownloadProgress;

    private UpdateManager GetManager()
    {
        if (_manager != null) return _manager;
        // new UpdateManager(source) 默认从当前 exe 目录加载（Update.exe 须在同目录）。
        _manager = new UpdateManager(_source);
        return _manager;
    }

    /// <summary>是否通过 Velopack 安装（Update.exe 存在于同目录）。</summary>
    public bool IsInstalled => SafeIsInstalled();

    /// <summary>是否已下载好更新但未重启应用（用户可能想稍后手动重启）。</summary>
    public bool IsUpdatePendingRestart
    {
        get
        {
            try { return GetManager().UpdatePendingRestart is not null; }
            catch { return false; }
        }
    }

    /// <summary>后台检查更新。完成后通过 <see cref="UpdateAvailable"/> 通知订阅者。</summary>
    public async Task CheckAsync()
    {
        try
        {
            var mgr = GetManager();
            if (!mgr.IsInstalled) return;

            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null) return;

            HasPendingUpdate = true;
            PendingVersion = update.TargetFullRelease.Version.ToString();
            PendingReleaseNotes = update.TargetFullRelease.NotesMarkdown ?? string.Empty;
            UpdateAvailable?.Invoke(PendingVersion, PendingReleaseNotes);
        }
        catch
        {
            // 静默失败，不影响启动
        }
    }

    /// <summary>
    /// 下载当前 pending 更新。完成后调用方决定是否 <see cref="ApplyAndRestart"/>。
    /// 进度通过 <see cref="DownloadProgress"/> 推送。
    /// </summary>
    /// <returns>下载是否成功。</returns>
    public async Task<bool> DownloadAsync()
    {
        if (!HasPendingUpdate) return false;
        try
        {
            var mgr = GetManager();
            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null) return false;

            await mgr.DownloadUpdatesAsync(update, p =>
            {
                DownloadProgress?.Invoke(p);
            });
            _pendingApply = update;
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[velopack] 下载失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private Velopack.UpdateInfo? _pendingApply;

    /// <summary>
    /// 应用已下载的更新并重启。<b>调用前必须确保 UI 状态已保存</b>——本调用会让当前进程立即退出。
    /// </summary>
    public void ApplyAndRestart()
    {
        var mgr = GetManager();
        // 优先用缓存的 UpdateInfo；如果没有（比如启动期直接调用），先同步拉一次。
        var toApply = _pendingApply ?? mgr.CheckForUpdatesAsync().GetAwaiter().GetResult();
        if (toApply == null)
        {
            LogService.Instance.AddLog("[velopack] ApplyAndRestart: 没有可应用的更新");
            return;
        }
        // ApplyUpdatesAndRestart 内部会：1) 把当前进程退出码设为约定的重启标志
        // 2) 启动同目录的 Update.exe，由它执行原子替换 → 启新版本 → 把退出码还原。
        mgr.ApplyUpdatesAndRestart(toApply);
    }

    private bool SafeIsInstalled()
    {
        try { return GetManager().IsInstalled; }
        catch { return false; }
    }
}