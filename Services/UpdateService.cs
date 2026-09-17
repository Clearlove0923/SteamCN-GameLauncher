using System.Net.Http;
using System.Text.Json;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

/// <summary>
/// 查询当前 GitHub 仓库的 Release、比较 tag 版本并触发更新通知事件。
/// 启动检查失败不会影响应用；Debug 模式仍可从本地 version.json 测试强制更新和时间闸门。
/// </summary>
public sealed class UpdateService
{
    public static UpdateService Instance { get; } = new();
    private UpdateService() { }

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Violet0923/SteamCN-GameLauncher/releases/latest";
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/Violet0923/SteamCN-GameLauncher/releases?per_page=20";
    private const string ReleasesPageUrl =
        "https://github.com/Violet0923/SteamCN-GameLauncher/releases";
    private const string DebugLocalUrl = "http://127.0.0.1:9090/version.json";

    /// <summary>发现新版本时触发。参数：(message, downloadUrl, forceUpdate)。</summary>
    public event Action<string, string, bool>? UpdateAvailable;

    private string? _cachedMessage;
    private string? _cachedDownloadUrl;
    private bool _cachedForceUpdate;
    public bool HasPendingUpdate { get; private set; }
    public DateTimeOffset? PendingGateUntil { get; private set; }
    public bool LastCheckSucceeded { get; private set; }
    public string? LastCheckError { get; private set; }

    private static readonly HttpClient _httpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static AppSettings LoadSettings() => new SettingsService().Load();
    private static bool IsDebugMode => LoadSettings().DebugMode;
    private static bool IsBetaChannel => LoadSettings().BetaChannel;

    public async Task CheckUpdateAsync()
    {
        try
        {
            PendingGateUntil = null;
            LastCheckSucceeded = false;
            LastCheckError = null;

            var debug = IsDebugMode;
            string remoteVersion;
            string message;
            string downloadUrl;
            bool forceUpdate;

            if (debug)
            {
                var json = await FetchJsonAsync(DebugLocalUrl).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<VersionInfo>(json, _jsonOptions)
                    ?? throw new InvalidDataException("本地 version.json 无效。");
                remoteVersion = info.Version;
                message = info.Message;
                downloadUrl = string.IsNullOrWhiteSpace(info.DownloadUrl.Global)
                    ? info.DownloadUrl.Domestic : info.DownloadUrl.Global;
                forceUpdate = info.ForceUpdate;

                if (!string.IsNullOrWhiteSpace(info.AvailableAfter)
                    && DateTimeOffset.TryParse(info.AvailableAfter, out var gateTime)
                    && DateTimeOffset.Now < gateTime)
                {
                    PendingGateUntil = gateTime;
                    LastCheckSucceeded = true;
                    return;
                }
            }
            else
            {
                var release = await GetLatestReleaseAsync(IsBetaChannel).ConfigureAwait(false);
                remoteVersion = release.TagName;
                message = string.IsNullOrWhiteSpace(release.Body)
                    ? release.Name ?? $"发现新版本 {release.TagName}"
                    : release.Body;
                downloadUrl = IsValidReleaseUrl(release.HtmlUrl) ? release.HtmlUrl : ReleasesPageUrl;
                // GitHub Release 没有强制更新字段；线上 Release 一律按普通更新处理。
                forceUpdate = false;
            }

            LastCheckSucceeded = true;
            if (!ReleaseVersionComparer.IsNewer(remoteVersion, AppInfo.FullVersion)) return;

            _cachedMessage = message;
            _cachedDownloadUrl = downloadUrl;
            _cachedForceUpdate = forceUpdate;
            HasPendingUpdate = true;
            UpdateAvailable?.Invoke(message, downloadUrl, forceUpdate);
        }
        catch (Exception ex)
        {
            // 启动阶段保持静默；手动检查通过 LastCheckSucceeded 显示明确的网络错误。
            LastCheckError = ex.Message;
        }
    }

    public void ReplayIfPending()
    {
        if (HasPendingUpdate && _cachedMessage is not null && _cachedDownloadUrl is not null)
            UpdateAvailable?.Invoke(_cachedMessage, _cachedDownloadUrl, _cachedForceUpdate);
    }

    /// <summary>
    /// 正式渠道使用 latest 端点（自动排除草稿与预发布）；测试渠道选择最新非草稿 Release。
    /// </summary>
    private static async Task<GitHubReleaseInfo> GetLatestReleaseAsync(bool includePrerelease)
    {
        var json = await FetchJsonAsync(includePrerelease ? ReleasesApiUrl : LatestReleaseApiUrl)
            .ConfigureAwait(false);
        if (!includePrerelease)
        {
            return JsonSerializer.Deserialize<GitHubReleaseInfo>(json, _jsonOptions)
                ?? throw new InvalidDataException("GitHub Release 响应无效。");
        }

        var releases = JsonSerializer.Deserialize<List<GitHubReleaseInfo>>(json, _jsonOptions) ?? [];
        return GitHubReleaseSelector.SelectLatest(releases, includePrerelease: true)
            ?? throw new InvalidDataException("仓库中没有可用的 GitHub Release。");
    }

    private static async Task<string> FetchJsonAsync(string url)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _httpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SteamCN-GameLauncher/{AppInfo.Version.TrimStart('v')}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool IsValidReleaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
}
