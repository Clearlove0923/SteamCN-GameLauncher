using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services.Home;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

// 端到端测试:
//   1. 启动 Python Worker(若 HOMECONTENT_VENV 已设置,使用该 venv;否则用 %TEMP%\home-content-env)
//   2. 等待 8765 可连接
//   3. 用 HttpHomeContentTransport POST /v1/home-content
//   4. 断言 schema/banners/news/background 都符合契约
//   5. 退出时关闭 Worker

const int Port = 8765;
var endpoint = new Uri($"http://127.0.0.1:{Port}/v1/home-content");
var venv = Environment.GetEnvironmentVariable("HOMECONTENT_VENV")
           ?? Path.Combine(Path.GetTempPath(), "home-content-env");
var pythonExe = Path.Combine(venv, "Scripts", "python.exe");
// python 源码根目录优先用环境变量 HOMECONTENT_CWD,避免 OutDir 变化时改路径。
var workerCwd = Environment.GetEnvironmentVariable("HOMECONTENT_CWD")
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..", "..", "..", "python"));

Process? worker = null;
var startedByUs = false;
try
{
    if (!IsReachable(Port))
    {
        Check(File.Exists(pythonExe), $"python venv exists at {pythonExe}");
        Check(Directory.Exists(workerCwd), $"python source root exists at {workerCwd}");

        worker = Process.Start(new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = "-m home_content.server.main",
            WorkingDirectory = workerCwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        startedByUs = true;
        Console.WriteLine($"Started Python Worker pid={worker?.Id}");

        if (!WaitForPort(Port, TimeSpan.FromSeconds(20)))
        {
            var err = worker?.StandardError.ReadToEndAsync();
            throw new Exception($"Worker did not start within timeout. stderr: {err?.GetAwaiter().GetResult()}");
        }
    }
    else
    {
        Console.WriteLine("Worker already reachable on port " + Port);
    }

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var transport = new HttpHomeContentTransport(httpClient, endpoint);

    var envelope = await transport.FetchAsync(new HomeContentRequest
    {
        RequestId = "e2e-request-1",
        GameId = "preview-e2e",
        ProviderId = "hoyoplay-json",
        Locale = "zh-CN",
    });

    Check(envelope.SchemaVersion == 1, "schemaVersion == 1");
    Check(envelope.ProviderId == "hoyoplay-json", "providerId round-trip");
    Check(envelope.RequestId == "e2e-request-1", "requestId round-trip");
    Check(envelope.Content.Background?.VideoUrl?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) == true,
        "background video URL present");
    Check(envelope.Content.Background?.ImageUrl?.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) == true,
        "background poster URL present");
    Check(envelope.Content.Banners.Count >= 1, "at least one banner");
    Check(envelope.Content.Banners.All(b => !string.IsNullOrWhiteSpace(b.Id)), "all banners have id");
    Check(envelope.Content.News.Count >= 1, "at least one news item");
    Check(envelope.Content.News.All(n => !string.IsNullOrWhiteSpace(n.Title)), "all news have title");

    // 验证未知 provider 会得到 4xx,从而客户端能区分可恢复错误
    try
    {
        await transport.FetchAsync(new HomeContentRequest
        {
            RequestId = "e2e-request-2",
            GameId = "preview-e2e",
            ProviderId = "does-not-exist",
            Locale = "zh-CN",
        });
        throw new Exception("FAILED: unknown provider must be rejected");
    }
    catch (HomeContentTransportException ex) when (ex.Message.Contains("400", StringComparison.Ordinal))
    {
        checks++;
        Console.WriteLine("PASS: unknown providerId rejected with 400");
    }
}
finally
{
    if (startedByUs && worker is { HasExited: false })
    {
        try { worker.Kill(entireProcessTree: true); } catch { }
    }
    worker?.Dispose();
}

Console.WriteLine($"All {checks} checks passed.");

static bool IsReachable(int port)
{
    try
    {
        using var client = new TcpClient();
        var task = client.ConnectAsync("127.0.0.1", port);
        return task.Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
    }
    catch
    {
        return false;
    }
}

static bool WaitForPort(int port, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (IsReachable(port)) return true;
        Thread.Sleep(250);
    }
    return false;
}