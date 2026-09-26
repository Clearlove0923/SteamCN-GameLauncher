using System.Diagnostics;
using System.Net.Sockets;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// 在 launcher 进程内拉起并监管 Python Worker（FastAPI / uvicorn）子进程，
/// 让 C# 端 <see cref="FastApiHomeContentService"/> 能拿到真实的 banner / 背景视频 / 资讯数据。
///
/// 生命周期：
/// <list type="bullet">
///   <item><c>StartAsync</c> 探测端口空闲后 <c>Process.Start</c> + 等端口起来</item>
///   <item>运行时 stdout/stderr 通过 <see cref="OutputReceived"/> 暴露给 <see cref="LogService"/></item>
///   <item>进程退出触发 <see cref="Exited"/> 事件，错误码写入</item>
///   <item><c>Stop</c> 尝试 graceful kill（CloseMainWindow → Kill），超时由 <see cref="AppSettings.PythonWorkerShutdownTimeoutSeconds"/> 控制</item>
/// </list>
///
/// 探测策略（按优先级）：
/// <list type="number">
///   <item><see cref="AppSettings.HomeContentWorkerBaseUrl"/> 端口已经在听 → 直接复用，不 spawn</item>
///   <item><see cref="AppSettings.PythonExecutablePath"/> 非空且文件存在 → 用之</item>
///   <item>否则按 PATH + 常见安装目录探测 <c>python.exe</c> / <c>python3.exe</c> / <c>py</c> 等</item>
/// </list>
///
/// 失败语义：任何一步失败（找不到 python / Process.Start 抛 / 端口超时 / 子进程秒退）
/// 都返回 <c>false</c>，调用方应继续走 <see cref="PreviewHomeContentService"/> 兜底，
/// 不阻塞 launcher 启动。
/// </summary>
public sealed class PythonWorkerSpawner : IDisposable
{
    private readonly AppSettings _settings;
    private readonly LogService _logService;
    private readonly int _port;
    private Process? _process;
    private bool _disposed;
    private bool _started;
    private bool _ownsProcess;

    /// <summary>
    /// Worker 是否仍在运行。
    /// fast-path（端口已被外部进程占用）时 <see cref="_process"/> 仍是 null，
    /// 但调用方已经把"存在外部进程在跑"视为可服务，因此本属性同样返回 true。
    /// </summary>
    public bool IsRunning => _started && (_process is null || !_process.HasExited);

    /// <summary>Worker 是否由本进程拉起（区别于"外部已存在"）。</summary>
    public bool OwnsProcess => _ownsProcess;

    /// <summary>stdout/stderr 一行到达时触发，调用方负责落 LogService。</summary>
    public event Action<string>? OutputReceived;

    /// <summary>子进程退出时触发，参数为 exit code。</summary>
    public event Action<int>? Exited;

    public PythonWorkerSpawner(AppSettings settings, LogService logService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logService);
        _settings = settings;
        _logService = logService;
        _port = ExtractPort(settings.HomeContentWorkerBaseUrl);
    }

    /// <summary>
    /// 尝试拉起 Python Worker：先探测端口，若已被占用则视为"外部已在运行"直接返回 true；
    /// 否则探测 python 解释器 + spawn 进程并等待端口可连。
    /// </summary>
    /// <returns>true 表示 launcher 可以走 <see cref="FastApiHomeContentService"/>；false 表示需要降级。</returns>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return _process is null || !_process.HasExited;
        }

        // 调用方（App.OnLaunched）已经决定要 spawn；这里保留二次保护，避免
        // 未来从别处复用本类时静默忽略开关。
        if (!_settings.SpawnPythonWorkerOnLaunch)
        {
            _logService.AddLog("[worker] SpawnPythonWorkerOnLaunch=false; StartAsync aborts");
            return false;
        }

        if (await IsPortListeningAsync(_port, cancellationToken).ConfigureAwait(false))
        {
            _logService.AddLog($"[worker] port {_port} already listening; skip spawn");
            _started = true;
            _ownsProcess = false;
            return true;
        }

        var pythonExe = ResolvePythonExecutable();
        if (pythonExe is null)
        {
            _logService.AddLog("[worker] python.exe not found; skip spawn (fall back to PreviewHomeContentService)");
            return false;
        }

        var workingDir = ResolveWorkingDirectory();
        var envPath = string.IsNullOrWhiteSpace(_settings.PythonHomeContentPath)
            ? Path.Combine(workingDir, "python")
            : _settings.PythonHomeContentPath;

        var arguments = $"-m home_content.server.main --port {_port}";

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.EnvironmentVariables["PYTHONPATH"] = envPath;
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        psi.EnvironmentVariables["HOMECONTENT_PORT"] = _port.ToString();

        Process proc;
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                OutputReceived?.Invoke($"stdout: {e.Data}");
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                OutputReceived?.Invoke($"stderr: {e.Data}");
            };
            proc.Exited += (_, _) =>
            {
                var code = -1;
                try { code = proc.ExitCode; } catch { /* exited */ }
                Exited?.Invoke(code);
                // 注意：不在这里 Dispose proc，留给 Stop() / Dispose() 统一释放，
                // 否则 StartAsync 的端口轮询循环里再访问 proc.HasExited 会抛 ANE。
            };

            if (!proc.Start())
            {
                _logService.AddLog("[worker] Process.Start returned false; skip spawn");
                proc.Dispose();
                return false;
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _process = proc;
            _ownsProcess = true;
            _started = true;
            _logService.AddLog($"[worker] spawned python worker pid={proc.Id} port={_port} cwd={workingDir}");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"[worker] failed to spawn: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, _settings.PythonWorkerStartupTimeoutSeconds));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (proc.HasExited)
            {
                int code = -1;
                try { code = proc.ExitCode; } catch { /* may have been disposed already */ }
                _logService.AddLog($"[worker] process exited prematurely with code {code}");
                _ownsProcess = false;
                return false;
            }
            if (await IsPortListeningAsync(_port, cancellationToken).ConfigureAwait(false))
            {
                _logService.AddLog($"[worker] port {_port} is listening; ready for FastApiHomeContentService");
                return true;
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        _logService.AddLog($"[worker] port {_port} did not become available within {_settings.PythonWorkerStartupTimeoutSeconds}s; killing child");
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch { /* best effort */ }
        try { proc.Dispose(); } catch { /* best effort */ }
        _process = null;
        _ownsProcess = false;
        return false;
    }

    /// <summary>
    /// 优雅关闭 Worker。先 CloseMainWindow（对 uvicorn 无 GUI 的子进程通常无效），
    /// 然后 <see cref="PythonWorkerShutdownTimeoutSeconds"/> 秒内等退出，否则强杀整棵进程树。
    /// 不抛异常，所有失败吞掉记 log。
    /// </summary>
    public void Stop()
    {
        if (_disposed) return;
        var proc = _process;
        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
            {
                try { proc.CloseMainWindow(); }
                catch { /* not GUI; ignore */ }
            }
        }
        catch { /* best effort */ }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, _settings.PythonWorkerShutdownTimeoutSeconds));
        while (!proc.HasExited && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }

        if (!proc.HasExited)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (Exception ex)
            {
                _logService.AddLog($"[worker] kill failed: {ex.GetType().Name}: {ex.Message}");
            }
            try { proc.WaitForExit(2000); } catch { /* best effort */ }
        }

        try { proc.Dispose(); } catch { /* best effort */ }
        _process = null;
        _logService.AddLog("[worker] stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private static int ExtractPort(string baseUrl)
    {
        try
        {
            var uri = new Uri(baseUrl);
            return uri.Port;
        }
        catch
        {
            return 8765;
        }
    }

    /// <summary>TCP connect 探测端口，避免 HTTP 握手开销。</summary>
    private static async Task<bool> IsPortListeningAsync(int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            var timeout = Task.Delay(300, ct);
            var winner = await Task.WhenAny(connectTask, timeout).ConfigureAwait(false);
            if (winner == connectTask && client.Connected)
            {
                return true;
            }
            try { client.Close(); } catch { /* ignore */ }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? ResolvePythonExecutable()
    {
        // 1) 用户显式覆盖。
        if (!string.IsNullOrWhiteSpace(_settings.PythonExecutablePath) &&
            IsUsablePythonExecutable(_settings.PythonExecutablePath))
        {
            return _settings.PythonExecutablePath;
        }

        // 2) Embedded Python（launcher 自带的 python-build-standalone 解压目录）。
        //    这是零依赖路径：用户拷走 launcher 包就走，不需要装 Python。
        //    embedded 标记 = AppContext.BaseDirectory\python-runtime\python.exe 存在；
        //    由 csproj 的 <_EmbeddedPython> 私有 item group + CopyEmbeddedPython /
        //    CopyEmbeddedPythonToPublish target 在 Build/Publish 完成后复制到 OutDir。
        //    （WinUI3 下 <Content Include="python-runtime/**/*"> 会被 PRI 生成器当成语言限定符处理，
        //    触发几百条 PRI249 warning；<None Include> 默认不复制，所以走自定义 Target 最稳。）
        var embedded = Path.Combine(AppContext.BaseDirectory, "python-runtime", "python.exe");
        if (IsUsablePythonExecutable(embedded))
        {
            _logService.AddLog($"[worker] using embedded python at {embedded}");
            return embedded;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string? ResolveOnPath(string fileName)
        {
            foreach (var dir in pathDirs)
            {
                try
                {
                    var full = Path.Combine(dir, fileName);
                    if (IsUsablePythonExecutable(full)) return full;
                }
                catch
                {
                    // PATH 中可能有非 ASCII / 非法路径条目，跳过。
                }
            }
            return null;
        }

        // 3) PATH 上常见的 python 名称（Windows 不区分大小写，但显式列几个最常见的）。
        foreach (var name in new[] { "python.exe", "python3.exe", "python", "python3" })
        {
            var hit = ResolveOnPath(name);
            if (hit is not null) return hit;
        }

        // 4) 常见安装目录（uv-installer / python.org installer / Microsoft Store）。
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(localApp, "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python310", "python.exe"),
            Path.Combine(programFiles, "Python313", "python.exe"),
            Path.Combine(programFiles, "Python312", "python.exe"),
            Path.Combine(programFiles, "Python311", "python.exe"),
            Path.Combine(programFiles, "Python310", "python.exe"),
            @"C:\Python313\python.exe",
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
        };
        foreach (var c in candidates)
        {
            try
            {
                if (IsUsablePythonExecutable(c)) return c;
            }
            catch
            {
                // 跳过无权访问的路径。
            }
        }
        return null;
    }

    /// <summary>
    /// Windows may put zero-byte python.exe aliases in WindowsApps even when no
    /// Python runtime is installed. Process.Start then exits with code 9009.
    /// Reject those aliases and empty/corrupt files before spawning the Worker.
    /// </summary>
    private static bool IsUsablePythonExecutable(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.Contains(
                    $"{Path.DirectorySeparatorChar}Microsoft{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(fullPath) && new FileInfo(fullPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 推断 Worker 工作目录：标准仓库布局下应能找到 <c>&lt;repo&gt;/python/home_content/server/main.py</c>。
    /// 找不到就 fallback 到 launcher 当前目录，至少不会 spawn 失败。
    /// </summary>
    private static string ResolveWorkingDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var marker = Path.Combine(dir, "python", "home_content", "server", "main.py");
            if (File.Exists(marker))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName ?? string.Empty;
        }
        return AppContext.BaseDirectory;
    }
}
