using System.Collections.ObjectModel;
using System.Text;

namespace SteamCNGameLauncher.Services;

public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private readonly string _logFilePath;
    private readonly object _writeLock = new();
    private Func<bool>? _hasUiAccess;
    private Func<Action, bool>? _enqueueUi;
    private const int MaxInMemoryLogs = 1000;

    public ObservableCollection<string> Logs { get; } = new();

    public void AttachDispatcher(Func<bool> hasUiAccess, Func<Action, bool> enqueueUi)
    {
        // 磁盘日志可从任意线程写；绑定集合只能在 WinUI UI 线程更新。
        _hasUiAccess = hasUiAccess ?? throw new ArgumentNullException(nameof(hasUiAccess));
        _enqueueUi = enqueueUi ?? throw new ArgumentNullException(nameof(enqueueUi));
    }

    private LogService()
    {
        var logDir = ResolveLogDirectory();
        CleanupOldLogs(logDir, keep: 20);
        _logFilePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        AppendToFile($"==== Session Start: {AppInfo.FullVersion} @ {DateTime.Now:O} ====");
    }

    public void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        AppendToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        DispatchToUi(() =>
        {
            Logs.Add(entry);
            if (Logs.Count > MaxInMemoryLogs)
                Logs.RemoveAt(0);
        });
    }

    public void Clear()
    {
        DispatchToUi(Logs.Clear);
    }

    private void DispatchToUi(Action update)
    {
        var enqueueUi = _enqueueUi;
        if (enqueueUi is not null && _hasUiAccess is { } hasUiAccess)
        {
            try
            {
                if (!hasUiAccess())
                {
                    // 窗口可能已关闭；磁盘日志已经写入，UI 队列失效不影响退出。
                    enqueueUi(() => TryUpdateMemory(update));
                    return;
                }
            }
            catch { return; /* 关闭过程中 UI 队列可能已经不可用。 */ }
        }
        TryUpdateMemory(update);
    }

    private static void TryUpdateMemory(Action update)
    {
        try { update(); }
        catch { /* 日志观察者异常不能打断窗口退出。 */ }
    }

    private static string ResolveLogDirectory()
    {
        var exeDir = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(exeDir);
            var probe = Path.Combine(exeDir, ".writetest");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return exeDir;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamCN-GameLauncher", "logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static void CleanupOldLogs(string directory, int keep)
    {
        try
        {
            var stale = Directory.GetFiles(directory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep);
            foreach (var f in stale)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    private void AppendToFile(string line)
    {
        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
