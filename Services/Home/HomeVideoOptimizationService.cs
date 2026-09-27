using System.Collections.Concurrent;
using System.Diagnostics;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// 为已下载到首页缓存的 WebM 背景生成易于系统解码的 H.264 副本。
/// 原文件始终保留：工具缺失、转换失败或用户切换游戏时，播放器仍可使用原视频。
/// </summary>
public sealed class HomeVideoOptimizationService
{
    public static HomeVideoOptimizationService Instance { get; } = new();

    private readonly ConcurrentDictionary<string, Task<string?>> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _executablePath;

    public HomeVideoOptimizationService(string? executablePath = null) =>
        _executablePath = executablePath ?? Path.Combine(AppContext.BaseDirectory, "python-runtime", "Lib",
            "site-packages", "imageio_ffmpeg", "binaries", "ffmpeg-win-x86_64-v7.1.exe");

    public string? FindOptimized(string source)
    {
        if (!IsEligible(source)) return null;
        var output = OutputPath(source);
        // 文件名包含原文件大小、修改时间和画质版本；更换源文件或参数后不会误用旧副本。
        if (!File.Exists(output) || new FileInfo(output).Length <= 1024) return null;
        try { File.SetLastAccessTimeUtc(output, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return output;
    }

    public Task<string?> OptimizeAsync(string source, CancellationToken cancellationToken = default)
    {
        var existing = FindOptimized(source);
        if (existing is not null) return Task.FromResult<string?>(existing);
        if (!IsEligible(source)) return Task.FromResult<string?>(null);
        // 同一缓存视频同时收到页面刷新和窗口播放请求时，只运行一次转换进程。
        return _jobs.GetOrAdd(source, path => RunAndReleaseAsync(path, cancellationToken));
    }

    private async Task<string?> RunAndReleaseAsync(string source, CancellationToken cancellationToken)
    {
        try { return await ConvertAsync(source, cancellationToken).ConfigureAwait(false); }
        finally { _jobs.TryRemove(source, out _); }
    }

    private async Task<string?> ConvertAsync(string source, CancellationToken cancellationToken)
    {
        var tool = _executablePath;
        if (!File.Exists(tool)) return null;
        var output = OutputPath(source);
        // 先写临时文件，进程正常结束后才原子替换正式副本，避免播放器读到半成品。
        // 媒体缓存清理会跳过 .tmp 后缀。
        var temporary = output + ".tmp";
        try
        {
            var info = new ProcessStartInfo(tool)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            // CRF 18 优先保留细节；限制线程数并降低进程优先级，尽量不抢占正在播放的解码资源。
            foreach (var argument in new[]
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", source,
                "-map", "0:v:0", "-an", "-c:v", "libx264", "-preset", "veryfast",
                "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                "-threads", "2", "-f", "mp4", temporary,
            }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return null;
            using var stop = cancellationToken.Register(() =>
            {
                // 页面离开或窗口关闭时连同子进程一起结束，避免后台继续占用 CPU。
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var details = await error.ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length <= 1024)
            {
                LogService.Instance.AddLog($"[首页背景] 动画缓存转换失败：{details.Trim()}");
                return null;
            }
            File.Move(temporary, output, overwrite: true);
            return output;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogService.Instance.AddLog($"[首页背景] 动画缓存转换失败：{ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }

    private static bool IsEligible(string source)
    {
        if (!Path.GetExtension(source).Equals(".webm", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            return false;
        // 仅处理软件自己管理的缓存，不改写用户游戏安装目录中的原始资源。
        var root = Path.GetFullPath(HomeContentServiceFactory.MediaCacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(source).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string OutputPath(string source)
    {
        var info = new FileInfo(source);
        return Path.Combine(info.DirectoryName!, $"{Path.GetFileNameWithoutExtension(source)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}-crf18.optimized.mp4");
    }
}
