using System.Diagnostics;
using System.Runtime.InteropServices;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>
/// 启动随 Inno Setup 安装到应用目录的 Kachina 更新器。
/// 自动发现和手动检查共用同一个 Kachina 窗口；确认、下载、安装进度和启动按钮均在其中显示。
/// </summary>
public sealed class KachinaUpdateService
{
    public const string UpdaterFileName = KachinaSessionPackage.UpdaterFileName;

    public static KachinaUpdateService Instance { get; } = new();

    private KachinaUpdateService()
    {
    }

    public string UpdaterPath => Path.Combine(AppContext.BaseDirectory, UpdaterFileName);
    private Process? _activeProcess;
    private readonly SemaphoreSlim _launchLock = new(1, 1);

    public async Task<string?> ShowAsync(string? sourceId, LauncherUpdateInfo? update = null)
    {
        await _launchLock.WaitAsync();
        try
        {
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Refresh();
                if (_activeProcess.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(_activeProcess.MainWindowHandle, 9);
                    SetForegroundWindow(_activeProcess.MainWindowHandle);
                }
                return null;
            }
            _activeProcess?.Dispose();
            _activeProcess = null;
            var normalizedSource = UpdateSourcePolicy.Normalize(sourceId);
            var updaterPath = UpdaterPath;
            if (!File.Exists(updaterPath))
            {
                return "更新程序不存在，请从发布页重新下载安装。";
            }

            if (update is null)
            {
                try
                {
                    update = await UpdateService.Instance.GetLatestUpdateAsync(normalizedSource,
                        new SettingsService().Load().BetaChannel);
                }
                catch (Exception ex)
                {
                    // 检测服务故障不阻止用户打开更新窗口、切换来源后重试。
                    LogService.Instance.AddLog($"[更新] 暂时无法读取版本说明：{ex.Message}");
                }
            }
            var sessionPath = await KachinaSessionPackage.CreateAsync(updaterPath, update);
            var startInfo = new ProcessStartInfo
            {
                FileName = sessionPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };
            // 不使用 -I / -S：先由用户在窗口中确认，下载和安装结束后保留“启动”按钮。
            startInfo.ArgumentList.Add("-D");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(normalizedSource);

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return "Windows 未能启动更新程序。";
            }
            _activeProcess = process;
            _ = CleanupSessionAsync(process, sessionPath);

            LogService.Instance.AddLog(
                $"[更新] 已启动 Kachina，来源：{UpdateSourcePolicy.GetDisplayName(normalizedSource)}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog(
                $"[更新] Kachina 启动失败：{ex.GetType().Name}: {ex.Message}");
            return ex.Message;
        }
        finally { _launchLock.Release(); }
    }

    private static async Task CleanupSessionAsync(Process process, string path)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            File.Delete(path);
            // 仅删除本次创建的空目录；不递归删除，保留更新器写入的日志等诊断文件。
            Directory.Delete(Path.GetDirectoryName(path)!);
        }
        catch { /* 主程序被更新器关闭时，临时副本由系统临时文件清理策略处理。 */ }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
