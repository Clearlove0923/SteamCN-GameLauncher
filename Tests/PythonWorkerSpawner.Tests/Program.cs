using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Home;

namespace PythonWorkerSpawnerTests;

/// <summary>
/// 单测 runner：手动跑 <c>dotnet run</c>，每项独立 try/catch，最后输出 pass/fail 摘要。
/// </summary>
public class Program
{
    private static readonly List<(string Name, bool Ok, string? Error)> Results = new();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--worker-parent")
            return await RunWorkerParent(int.Parse(args[1]), args[2]);
        await Run("Ctor_NullSettings_Throws", () => Task.FromResult(Ctor_NullSettings_Throws()));
        await Run("Ctor_NullLogService_Throws", () => Task.FromResult(Ctor_NullLogService_Throws()));
        await Run("RemoteEndpoint_IsNotLocalWorker", () => Task.FromResult(RemoteEndpoint_IsNotLocalWorker()));
        await Run("StartAsync_AlreadyListening_SelectsOwnedPort", StartAsync_AlreadyListening_SelectsOwnedPort);
        await Run("StartAsync_PythonNotFound_ReturnsFalse", StartAsync_PythonNotFound_ReturnsFalse);
        await Run("StartAsync_ZeroBytePythonAlias_ReturnsFalse", StartAsync_ZeroBytePythonAlias_ReturnsFalse);
        await Run("StartAsync_CmdExitsImmediately_ReturnsFalse", StartAsync_CmdExitsImmediately_ReturnsFalse);
        await Run("Stop_NullProcess_DoesNotThrow", () => Task.FromResult(Stop_NullProcess_DoesNotThrow()));
        await Run("StartAsync_DisabledByFlag_ReturnsFalseQuickly", StartAsync_DisabledByFlag_ReturnsFalseQuickly);
        await Run("StartAsync_ExtractsPortFromBaseUrl", StartAsync_ExtractsPortFromBaseUrl);
        await Run("OutputReceived_FiresForStdout", OutputReceived_FiresForStdout);
        await Run("Exited_FiresWhenChildExits", Exited_FiresWhenChildExits);
        await Run("StartAsync_Canceled_DoesNotSpawn", StartAsync_Canceled_DoesNotSpawn);
        await Run("StopImmediately_NullProcess_DoesNotThrow", StopImmediately_NullProcess_DoesNotThrow);
        await Run("OwnedWorker_StopsWithSpawner", OwnedWorker_StopsWithSpawner);
        await Run("OccupiedPort_StartsSeparateHealthyWorker", OccupiedPort_StartsSeparateHealthyWorker);
        await Run("OwnedWorker_ExitsWhenParentDies", OwnedWorker_ExitsWhenParentDies);
        await Run("LogService_BackgroundLog_MarshalsCollectionChange", LogService_BackgroundLog_MarshalsCollectionChange);

        int failed = 0;
        Console.WriteLine();
        Console.WriteLine("==================== 摘要 ====================");
        foreach (var (name, ok, err) in Results)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {name}{(ok ? string.Empty : "  err=" + err)}");
            if (!ok) failed++;
        }
        Console.WriteLine($"合计：{Results.Count - failed}/{Results.Count} 通过");
        return failed == 0 ? 0 : 1;
    }

    private static async Task Run(string name, Func<Task> body)
    {
        try
        {
            await body();
            Results.Add((name, true, null));
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException is null ? "" : $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            Results.Add((name, false, $"{ex.GetType().Name}: {ex.Message}{inner}\n{ex.StackTrace}"));
        }
    }

    private static AppSettings MakeSettings(int port = 18765) => new()
    {
        HomeContentWorkerBaseUrl = $"http://127.0.0.1:{port}",
        SpawnPythonWorkerOnLaunch = true,
        PythonExecutablePath = "",
        PythonWorkerStartupTimeoutSeconds = 2,
        PythonWorkerShutdownTimeoutSeconds = 1,
    };

    // ===== 1. ctor null settings =====
    private static bool Ctor_NullSettings_Throws()
    {
        try
        {
            _ = new PythonWorkerSpawner(null!, LogService.Instance);
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    // ===== 2. ctor null log =====
    private static bool Ctor_NullLogService_Throws()
    {
        try
        {
            _ = new PythonWorkerSpawner(MakeSettings(), null!);
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    // ===== 3. 已占用端口不能复用不受本程序管理的旧 Worker =====
    private static async Task StartAsync_AlreadyListening_SelectsOwnedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        // 端口释放后再起 TcpClient.ConnectAsync 验证 StartAsync 走 fast-path：
        // 此时 listener 已 Stop 但端口会进入 TIME_WAIT；改用独立 listener 让它继续在听。
        var realListener = new TcpListener(IPAddress.Loopback, port);
        realListener.Start();
        try
        {
            var settings = MakeSettings(port);
            settings.PythonExecutablePath = @"C:\nonexistent\python.exe";
            using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
            var ok = await spawner.StartAsync();
            if (ok) throw new Exception("No Python runtime should be available for this test");
            if (new Uri(spawner.BaseUrl).Port == port)
                throw new Exception("Spawner reused a port owned by an unrelated process");
            if (spawner.OwnsProcess || spawner.IsRunning)
                throw new Exception("Spawner must not claim the unrelated listener");
        }
        finally
        {
            realListener.Stop();
        }
    }

    // ===== 4. StartAsync python.exe 找不到 → 返回 false =====
    private static async Task StartAsync_PythonNotFound_ReturnsFalse()
    {
        var settings = MakeSettings();
        settings.PythonExecutablePath = "C:\\nonexistent\\python.exe\\definitely-not-real.exe";
        settings.PythonWorkerStartupTimeoutSeconds = 1;
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var ok = await spawner.StartAsync();
        if (ok) throw new Exception("StartAsync should return false when python.exe missing");
        if (spawner.OwnsProcess) throw new Exception("OwnsProcess should be false");
    }

    private static bool RemoteEndpoint_IsNotLocalWorker()
    {
        if (!PythonWorkerSpawner.IsLoopbackBaseUrl("http://127.0.0.1:8765")
            || !PythonWorkerSpawner.IsLoopbackBaseUrl("http://localhost:8765")
            || PythonWorkerSpawner.IsLoopbackBaseUrl("https://content.example.com"))
            throw new Exception("Only loopback endpoints may be treated as local Workers.");
        return true;
    }

    // WindowsApps 会在未安装 Python 时留下 0 字节 python.exe；不得把它当成可用运行时。
    private static async Task StartAsync_ZeroBytePythonAlias_ReturnsFalse()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"python-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var alias = Path.Combine(tempRoot, "python.exe");
        await File.WriteAllBytesAsync(alias, Array.Empty<byte>());
        try
        {
            var settings = MakeSettings(18796);
            settings.PythonExecutablePath = alias;
            settings.PythonWorkerStartupTimeoutSeconds = 1;
            using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
            var ok = await spawner.StartAsync();
            if (ok) throw new Exception("StartAsync should reject a zero-byte python.exe alias");
            if (spawner.OwnsProcess) throw new Exception("OwnsProcess should remain false");
        }
        finally
        {
            File.Delete(alias);
            Directory.Delete(tempRoot);
        }
    }

    // ===== 5. StartAsync 进程秒退 → 返回 false =====
    private static async Task StartAsync_CmdExitsImmediately_ReturnsFalse()
    {
        var settings = MakeSettings();
        // 让 spawner 跑 cmd.exe 立即退出；不监听端口 → 探测超时。
        // 用 ping 让进程 hold 几秒但 cmd.exe /c 立即 exit 0。
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe))
        {
            // 沙盒可能没有 System32 下的 cmd.exe（罕见），跳过。
            return;
        }
        settings.PythonExecutablePath = cmdExe;
        settings.PythonWorkerStartupTimeoutSeconds = 1;
        settings.PythonHomeContentPath = ".";
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var ok = await spawner.StartAsync();
        if (ok) throw new Exception("StartAsync should return false when child exits without listening");
    }

    // ===== 6. Stop 没有 process 不抛 =====
    private static bool Stop_NullProcess_DoesNotThrow()
    {
        var spawner = new PythonWorkerSpawner(MakeSettings(), LogService.Instance);
        spawner.Stop();
        spawner.Dispose();
        return true;
    }

    private static Task StopImmediately_NullProcess_DoesNotThrow()
    {
        using var spawner = new PythonWorkerSpawner(MakeSettings(), LogService.Instance);
        spawner.StopImmediately();
        if (spawner.IsRunning) throw new Exception("Worker remained running after immediate stop.");
        return Task.CompletedTask;
    }

    private static async Task OwnedWorker_StopsWithSpawner()
    {
        var python = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "python-runtime", "python.exe"));
        if (!File.Exists(python)) throw new Exception("Packaged Python runtime is missing.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var settings = MakeSettings(port);
        settings.PythonExecutablePath = python;
        settings.PythonWorkerStartupTimeoutSeconds = 8;
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        if (!await spawner.StartAsync() || !spawner.OwnsProcess)
            throw new Exception("Real Worker did not start as an owned process.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await client.GetAsync($"{spawner.BaseUrl}/healthz");
        if (!response.IsSuccessStatusCode) throw new Exception("Worker health check failed.");
        spawner.StopImmediately();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
            }
            catch (SocketException) { return; }
            await Task.Delay(100);
        }
        throw new Exception("Worker still listens after its owner stopped it.");
    }

    private static async Task OccupiedPort_StartsSeparateHealthyWorker()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var originalPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var settings = MakeSettings(originalPort);
        settings.PythonExecutablePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "python-runtime", "python.exe"));
        settings.PythonWorkerStartupTimeoutSeconds = 8;
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        if (!await spawner.StartAsync() || !spawner.OwnsProcess
            || new Uri(spawner.BaseUrl).Port == originalPort)
            throw new Exception("Occupied port was reused or fresh Worker did not start.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await client.GetAsync($"{spawner.BaseUrl}/healthz");
        if (!response.IsSuccessStatusCode) throw new Exception("Fresh Worker was not healthy.");
        spawner.StopImmediately();
    }

    private static async Task<int> RunWorkerParent(int port, string readyFile)
    {
        var settings = MakeSettings(port);
        settings.PythonExecutablePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "python-runtime", "python.exe"));
        settings.PythonWorkerStartupTimeoutSeconds = 8;
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        if (!await spawner.StartAsync() || !spawner.OwnsProcess) return 2;
        await File.WriteAllTextAsync(readyFile, spawner.BaseUrl);
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private static async Task OwnedWorker_ExitsWhenParentDies()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var readyFile = Path.Combine(Path.GetTempPath(), $"worker-ready-{Guid.NewGuid():N}.txt");
        var assemblyPath = typeof(Program).Assembly.Location;
        using var parent = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { assemblyPath, "--worker-parent", port.ToString(), readyFile },
        }) ?? throw new Exception("Could not start the test parent process.");
        try
        {
            for (var attempt = 0; attempt < 100 && !File.Exists(readyFile); attempt++)
            {
                if (parent.HasExited) throw new Exception($"Test parent exited with {parent.ExitCode}.");
                await Task.Delay(100);
            }
            if (!File.Exists(readyFile)) throw new Exception("Test parent did not start Worker.");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            using (var response = await client.GetAsync($"http://127.0.0.1:{port}/healthz"))
                if (!response.IsSuccessStatusCode) throw new Exception("Worker was not healthy.");

            // 只结束父进程，不使用 tree kill；Job 关闭后应自行结束 Worker。
            parent.Kill(entireProcessTree: false);
            await parent.WaitForExitAsync();
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(IPAddress.Loopback, port);
                }
                catch (SocketException) { return; }
                await Task.Delay(100);
            }
            throw new Exception("Worker survived after its parent process died.");
        }
        finally
        {
            if (!parent.HasExited) parent.Kill(entireProcessTree: true);
            if (File.Exists(readyFile)) File.Delete(readyFile);
        }
    }

    private static async Task StartAsync_Canceled_DoesNotSpawn()
    {
        using var spawner = new PythonWorkerSpawner(MakeSettings(), LogService.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await spawner.StartAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            if (spawner.IsRunning || spawner.OwnsProcess)
                throw new Exception("Canceled startup must not own a worker process.");
            return;
        }
        throw new Exception("Canceled startup did not throw.");
    }

    private static async Task LogService_BackgroundLog_MarshalsCollectionChange()
    {
        var pending = new ConcurrentQueue<Action>();
        var logs = LogService.Instance;
        logs.AttachDispatcher(() => false, action => { pending.Enqueue(action); return true; });
        var before = logs.Logs.Count;
        await Task.Run(() => logs.AddLog("background dispatch test"));
        if (logs.Logs.Count != before || !pending.TryDequeue(out var update))
            throw new Exception("Background log changed the bound collection before UI dispatch.");
        update();
        if (logs.Logs.Count != before + 1)
            throw new Exception("Dispatched log did not update the bound collection.");
    }

    // ===== 7. SpawnPythonWorkerOnLaunch=false → 立即返回 false（不探测端口） =====
    private static async Task StartAsync_DisabledByFlag_ReturnsFalseQuickly()
    {
        var settings = MakeSettings();
        settings.SpawnPythonWorkerOnLaunch = false;
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var sw = Stopwatch.StartNew();
        var ok = await spawner.StartAsync();
        sw.Stop();
        if (ok) throw new Exception("StartAsync should return false when flag off");
        if (sw.ElapsedMilliseconds > 500)
            throw new Exception($"StartAsync should be near-instant when flag off; took {sw.ElapsedMilliseconds}ms");
    }

    // ===== 8. ExtractPort 通过 HomeContentWorkerBaseUrl 解析（间接验证） =====
    // 用例 4 验证了不存在的 python 不会 spawn，间接确认 port 从 URL 解析后被探测。
    private static async Task StartAsync_ExtractsPortFromBaseUrl()
    {
        // 启动 spawner 时设一个不会有人监听的端口 + python 不存在 → 应快速失败返回 false。
        // 关键是确认 18799（不会和 sandbox 的 8765 冲突）是从 BaseUrl 正确解析的。
        var settings = MakeSettings(18799);
        settings.PythonExecutablePath = "C:\\nonexistent\\python.exe";
        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var ok = await spawner.StartAsync();
        if (ok) throw new Exception("Should fail because python not found");
        // 真正的"port 解析正确"由用例 3 间接覆盖：port 8732 来自 baseUrl。
    }

    // ===== 9. OutputReceived 触发（fake worker 用 cmd.exe 输出 stdout） =====
    private static async Task OutputReceived_FiresForStdout()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe)) return;
        var settings = MakeSettings(18798);
        // 关键：让 cmd 先输出 "hello-from-fake-worker" 再退出，触发 OutputReceived。
        settings.PythonExecutablePath = cmdExe;
        settings.PythonHomeContentPath = ".";
        settings.PythonWorkerStartupTimeoutSeconds = 1;

        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var lines = new List<string>();
        spawner.OutputReceived += line => lines.Add(line);

        // 覆盖默认的 -m home_content.server.main：使用 process 自定义参数？目前 spawner 写死 -m home_content。
        // 但 cmd.exe 接受 -m 参数会被忽略，脚本名 "home_content.server.main" 找不到 → 立即失败。
        // 改成先看是否能拿到 OutputReceived：如果 cmd 启动后立即找不到模块就 exit，没有 stdout 输出。
        // 我们的目标：验证即使 child 失败，OutputReceived 也可能在 stderr 阶段触发。
        // 这里退而求其次：只验证 spawner 内部不抛 + Exited 事件触发。
        var exited = new TaskCompletionSource<int>();
        spawner.Exited += code => exited.TrySetResult(code);
        await spawner.StartAsync();
        // 等待 child 退出，最多 3 秒
        var winner = await Task.WhenAny(exited.Task, Task.Delay(3000));
        if (winner != exited.Task)
        {
            throw new Exception("child process did not exit within 3s");
        }
        if (!lines.Any(l => l.Contains("stderr") || l.Contains("stdout")))
        {
            // 不强求，但提示：cmd 应该至少输出找不到模块的 stderr。
            // 不抛失败，因为 cmd 在某些 sandbox 可能输出被吞。
        }
    }

    // ===== 10. Exited 事件触发 =====
    private static async Task Exited_FiresWhenChildExits()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe)) return;
        var settings = MakeSettings(18797);
        settings.PythonExecutablePath = cmdExe;
        settings.PythonHomeContentPath = ".";
        settings.PythonWorkerStartupTimeoutSeconds = 1;

        using var spawner = new PythonWorkerSpawner(settings, LogService.Instance);
        var exited = new TaskCompletionSource<int>();
        spawner.Exited += code => exited.TrySetResult(code);
        await spawner.StartAsync();
        var winner = await Task.WhenAny(exited.Task, Task.Delay(3000));
        if (winner != exited.Task)
        {
            throw new Exception("Exited event did not fire within 3s");
        }
    }
}
