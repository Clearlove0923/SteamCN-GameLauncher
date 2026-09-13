using System.Diagnostics;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

public enum SteamLaunchStatus
{
    Ready,
    Started,
    InvalidAppId,
    GameExecutableMissing,
    SteamExecutableMissing,
    SteamNotRunning,
    ManifestMissing,
    LaunchFailed,
}

public sealed record SteamLaunchResult(SteamLaunchStatus Status, string Message)
{
    public bool IsSuccess => Status is SteamLaunchStatus.Ready or SteamLaunchStatus.Started;
}

/// <summary>
/// 通过 Steam 客户端启动已配置的游戏。只有由 Steam 发起 AppID 启动，Steam 才能按该
/// AppID 跟踪游戏状态；本服务不直接运行真实游戏 EXE，也不伪造 Steam 在线状态。
/// </summary>
public sealed class SteamLaunchService
{
    private readonly SteamService _steamService;

    public SteamLaunchService(SteamService? steamService = null)
    {
        _steamService = steamService ?? new SteamService();
    }

    public SteamLaunchResult CheckReady(AppSettings settings, CustomManifestPreset preset)
    {
        if (!uint.TryParse(preset.AppId?.Trim(), out var appId) || appId == 0)
            return new(SteamLaunchStatus.InvalidAppId, "当前游戏没有有效的 AppID，请先完善游戏配置。");

        var gameExe = preset.ClientExePath?.Trim();
        if (string.IsNullOrWhiteSpace(gameExe) || !File.Exists(gameExe))
            return new(SteamLaunchStatus.GameExecutableMissing,
                "未找到真实游戏 EXE，请先在游戏配置页选择正确的游戏可执行文件。");

        if (ResolveSteamExecutable(settings.SteamInstallPath) == null)
            return new(SteamLaunchStatus.SteamExecutableMissing,
                "未找到 steam.exe，请先在设置页配置正确的 Steam 安装路径。");

        if (!IsSteamRunning())
            return new(SteamLaunchStatus.SteamNotRunning,
                "未检测到 Steam 进程。请先打开并登录 Steam，然后再开始游戏。");

        if (!ManifestExists(settings, appId))
            return new(SteamLaunchStatus.ManifestMissing,
                $"未找到 appmanifest_{appId}.acf，请先在游戏配置页生成配置文件并重启 Steam。");

        return new(SteamLaunchStatus.Ready, "Steam 已运行并登录，可以开始游戏。");
    }

    public SteamLaunchResult Launch(AppSettings settings, CustomManifestPreset preset)
    {
        var ready = CheckReady(settings, preset);
        if (!ready.IsSuccess) return ready;

        var appId = uint.Parse(preset.AppId.Trim());
        var steamExe = ResolveSteamExecutable(settings.SteamInstallPath)!;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = $"-applaunch {appId}",
                WorkingDirectory = Path.GetDirectoryName(steamExe),
                UseShellExecute = true,
            });
            return new(SteamLaunchStatus.Started,
                $"已请求 Steam 启动 {preset.Name}（AppID {appId}）。");
        }
        catch (Exception ex)
        {
            return new(SteamLaunchStatus.LaunchFailed, $"无法通过 Steam 启动游戏：{ex.Message}");
        }
    }

    private string? ResolveSteamExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredExe = Path.Combine(configuredPath.Trim(), "steam.exe");
            if (File.Exists(configuredExe)) return configuredExe;
        }

        var detectedPath = _steamService.DetectSteamInstallPath();
        if (string.IsNullOrWhiteSpace(detectedPath)) return null;
        var detectedExe = Path.Combine(detectedPath, "steam.exe");
        return File.Exists(detectedExe) ? detectedExe : null;
    }

    private static bool IsSteamRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("steam");
            try { return processes.Length > 0; }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    private bool ManifestExists(AppSettings settings, uint appId)
    {
        var libraries = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.SteamLibraryPath))
            libraries.Add(settings.SteamLibraryPath.Trim());
        libraries.AddRange(_steamService.DetectSteamLibraryPaths());

        return libraries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(path => File.Exists(Path.Combine(path, "steamapps", $"appmanifest_{appId}.acf")));
    }
}
