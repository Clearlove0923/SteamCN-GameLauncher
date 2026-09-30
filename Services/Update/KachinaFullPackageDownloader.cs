using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

public sealed record UpdatePackageDownloadProgress(long BytesReceived, long? TotalBytes);

/// <summary>
/// 顺序下载完整 Kachina 包，供不适合大量 HTTP Range 请求的更新源使用。
/// 下载完成后必须通过同一 Release 的 SHA256SUMS.txt 校验，才允许执行。
/// </summary>
internal static class KachinaFullPackageDownloader
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static async Task<string> DownloadAsync(
        LauncherUpdateInfo update,
        IProgress<UpdatePackageDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var packageName = UpdateSourcePolicy.GetPackageFileName(update.Version);
        var packageUrl = UpdateSourcePolicy.GetPackageUrl(UpdateSourceIds.Cnb, update.Version);
        var checksumUrl = UpdateSourcePolicy.GetChecksumUrl(UpdateSourceIds.Cnb, update.Version);
        var sessionDirectory = Path.Combine(
            Path.GetTempPath(), "SteamCN-GameLauncher-Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);
        var packagePath = Path.Combine(sessionDirectory, packageName);
        var partialPath = packagePath + ".part";

        try
        {
            var checksumText = await GetStringWithRetryAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            var expectedHash = ParseExpectedHash(checksumText, packageName);
            await DownloadFileWithRetryAsync(packageUrl, partialPath, progress, cancellationToken).ConfigureAwait(false);

            await using (var input = new FileStream(
                partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"CNB 更新包 SHA-256 校验失败。期望 {expectedHash.ToLowerInvariant()}，实际 {actualHash.ToLowerInvariant()}。");
            }

            File.Move(partialPath, packagePath);
            return packagePath;
        }
        catch
        {
            TryDelete(partialPath);
            TryDelete(packagePath);
            TryDeleteDirectory(sessionDirectory);
            throw;
        }
    }

    private static async Task<string> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (attempt >= 3 || !IsRetryable(response.StatusCode))
                throw new HttpRequestException(
                    $"下载校验文件失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null,
                    response.StatusCode);
            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DownloadFileWithRetryAsync(
        string url,
        string destination,
        IProgress<UpdatePackageDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                TryDelete(destination);
                using var response = await Client.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt >= 3 || !IsRetryable(response.StatusCode))
                        throw new HttpRequestException(
                            $"下载完整更新包失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null,
                            response.StatusCode);
                    await Task.Delay(GetRetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var totalBytes = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(
                    destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new UpdatePackageDownloadProgress(received, totalBytes));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (totalBytes is > 0 && received != totalBytes.Value)
                    throw new EndOfStreamException(
                        $"完整更新包下载不完整：应为 {totalBytes.Value} 字节，实际 {received} 字节。");
                return;
            }
            catch (Exception ex) when (attempt < 3 &&
                                       ex is HttpRequestException or IOException &&
                                       !cancellationToken.IsCancellationRequested)
            {
                TryDelete(destination);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static string ParseExpectedHash(string text, string packageName)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line, @"^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (match.Success && string.Equals(match.Groups["name"].Value, packageName,
                    StringComparison.OrdinalIgnoreCase))
                return match.Groups["hash"].Value;
        }
        throw new InvalidDataException($"SHA256SUMS.txt 中没有 {packageName} 的校验值。");
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt) =>
        response.Headers.RetryAfter?.Delta is { } delay && delay <= TimeSpan.FromMinutes(2)
            ? delay
            : TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path); } catch { }
    }
}
