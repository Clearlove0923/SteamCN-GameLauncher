using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Text;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace SteamCNGameLauncher;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    public static PythonWorkerSpawner? WorkerSpawner { get; private set; }
    public static Task? WorkerStartupTask { get; private set; }
    private readonly CancellationTokenSource _workerStartupCancellation = new();
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamCN-GameLauncher",
        "logs",
        "startup-crash.log");
    private static readonly string CrashLogFallbackPath = Path.Combine(
        AppContext.BaseDirectory,
        "startup-crash.log");

    public App()
    {
        WriteCrashLog("App.Constructor.Start", null);
        RegisterGlobalExceptionHandlers();
        _ = LogService.Instance;
        InitializeComponent();
        WriteCrashLog("App.Constructor.End", null);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Closed += (sender, e) => OnMainWindowClosed();
            // 页面等待此任务得到本次启动专属的 Worker 端口；窗口仍立即显示。
            WorkerStartupTask = TrySpawnWorkerAsync();
            MainWindow.Activate();

            // 后台静默检查更新，不阻塞启动
            _ = Task.Run(async () =>
            {
                try
                {
                    await UpdateService.Instance.CheckUpdateAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 静默忽略，不影响主流程
                }
            });

        }
        catch (Exception ex)
        {
            WriteCrashLog("OnLaunched", ex);
            throw;
        }
    }

    private async Task TrySpawnWorkerAsync()
    {
        // Worker 启动与窗口关闭并发时共用取消令牌，避免关闭后进程才创建成功。
        PythonWorkerSpawner? spawner = null;
        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            if (!settings.SpawnPythonWorkerOnLaunch)
            {
                LogService.Instance.AddLog("[worker] SpawnPythonWorkerOnLaunch=false; skip auto-spawn");
                return;
            }
            if (!PythonWorkerSpawner.IsLoopbackBaseUrl(settings.HomeContentWorkerBaseUrl))
            {
                LogService.Instance.AddLog("[worker] configured remote home-content endpoint; no local worker spawned");
                return;
            }

            _workerStartupCancellation.Token.ThrowIfCancellationRequested();
            spawner = new PythonWorkerSpawner(settings, LogService.Instance);
            spawner.OutputReceived += line => LogService.Instance.AddLog($"[worker] {line}");
            spawner.Exited += code => LogService.Instance.AddLog($"[worker] exited with code {code}");
            WorkerSpawner = spawner;

            var ok = await spawner.StartAsync(_workerStartupCancellation.Token).ConfigureAwait(false);
            if (!ok)
            {
                LogService.Instance.AddLog("[worker] StartAsync returned false; FastApiHomeContentService will fall back to PreviewHomeContentService");
            }
        }
        catch (OperationCanceledException)
        {
            spawner?.StopImmediately();
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[worker] spawn pipeline crashed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnMainWindowClosed()
    {
        // 不在 UI 线程等待 Python 优雅退出；先终止进程树，再完成应用退出。
        LogService.Instance.AddLog("[窗口] 已进入 Closed，正在结束 Worker");
        _workerStartupCancellation.Cancel();
        var spawner = WorkerSpawner;
        WorkerSpawner = null;
        try
        {
            spawner?.StopImmediately();
            spawner?.Dispose();
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"[worker] shutdown error: {ex.GetType().Name}: {ex.Message}");
        }
        LogService.Instance.AddLog("[窗口] 清理完成，正在退出应用");
        Exit();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        UnhandledException += OnApplicationUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Application.UnhandledException", e.Exception);
    }

    private void OnCurrentDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog("AppDomain.CurrentDomain.UnhandledException", ex);
        }
        else
        {
            WriteCrashLog("AppDomain.CurrentDomain.UnhandledException", null, e.ExceptionObject?.ToString());
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void WriteCrashLog(string source, Exception? ex, string? raw = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("==============================");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"PrimaryPath: {CrashLogPath}");
            sb.AppendLine($"FallbackPath: {CrashLogFallbackPath}");

            if (ex != null)
            {
                sb.AppendLine($"Exception: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
                if (ex is COMException comEx)
                {
                    sb.AppendLine($"COM ErrorCode: 0x{comEx.ErrorCode:X8}");
                }

                sb.AppendLine("StackTrace:");
                sb.AppendLine(ex.StackTrace ?? "<null>");

                if (ex.InnerException != null)
                {
                    sb.AppendLine("InnerException:");
                    sb.AppendLine(ex.InnerException.ToString());
                }
            }
            else
            {
                sb.AppendLine("Exception: <null>");
            }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                sb.AppendLine("Raw:");
                sb.AppendLine(raw);
            }

            var content = sb.ToString();
            if (!TryAppendText(CrashLogPath, content))
            {
                TryAppendText(CrashLogFallbackPath, content);
            }
        }
        catch
        {
        }
    }

    private static bool TryAppendText(string path, string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, content, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
