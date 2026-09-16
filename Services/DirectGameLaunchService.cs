using System.Diagnostics;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

/// <summary>不经过 Steam，使用当前游戏预设中的国服真实 EXE、工作目录和启动参数。</summary>
public sealed class DirectGameLaunchService
{
    public SteamLaunchResult CheckReady(CustomManifestPreset preset)
    {
        var executable = preset.ClientExePath?.Trim();
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new(SteamLaunchStatus.GameExecutableMissing,
                "未找到国服真实游戏 EXE，请先在游戏配置页选择正确的游戏可执行文件。");

        return new(SteamLaunchStatus.Ready, "国服游戏 EXE 已就绪，可以直接启动。");
    }

    public SteamLaunchResult Launch(CustomManifestPreset preset)
    {
        var ready = CheckReady(preset);
        if (!ready.IsSuccess)
            return ready;

        var executable = Path.GetFullPath(preset.ClientExePath.Trim());
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = preset.LaunchArguments?.Trim() ?? "",
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true,
            });
            return new(SteamLaunchStatus.Started,
                $"已直接启动 {preset.Name} 国服；本次启动未经过 Steam。");
        }
        catch (Exception ex)
        {
            return new(SteamLaunchStatus.LaunchFailed, $"无法直接启动国服游戏：{ex.Message}");
        }
    }
}
