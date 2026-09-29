using System.Diagnostics;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>
/// 启动随 Inno Setup 安装到应用目录的 Kachina 更新器。
/// 主程序只负责选择来源并启动更新器；下载、差异比较、文件替换和重启由 Kachina 完成。
/// </summary>
public sealed class KachinaUpdateService
{
    public const string UpdaterFileName = "SteamCN-GameLauncher.update.exe";

    public static KachinaUpdateService Instance { get; } = new();

    private KachinaUpdateService()
    {
    }

    public string UpdaterPath => Path.Combine(AppContext.BaseDirectory, UpdaterFileName);

    public bool TryStart(string? sourceId, out string? error)
    {
        error = null;
        var normalizedSource = UpdateSourcePolicy.Normalize(sourceId);
        var updaterPath = UpdaterPath;
        if (!File.Exists(updaterPath))
        {
            error = $"更新程序不存在：{updaterPath}";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };
            // -I 保留交互式进度界面，但跳过重新选择安装路径；更新仍需用户主动点击触发。
            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(normalizedSource);

            var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "Windows 未能启动更新程序。";
                return false;
            }

            LogService.Instance.AddLog(
                $"[更新] 已启动 Kachina，来源：{UpdateSourcePolicy.GetDisplayName(normalizedSource)}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LogService.Instance.AddLog(
                $"[更新] Kachina 启动失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
