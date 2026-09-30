using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services.Update;

namespace SteamCNGameLauncher.Services;

/// <summary>
/// 启动期轻量更新检测：按用户首选源读取 CNB 或 GitHub Release、比较 tag 版本并通知 UI。
///
/// <para>
/// 与 <see cref="SteamCNGameLauncher.Services.Update.KachinaUpdateService"/> 的分工：
/// </para>
/// <list type="bullet">
/// <item>本服务负责「发现版本」：HTTP 拉清单、比较 tag，并把版本与 Release 正文交给 UI。</item>
/// <item>KachinaUpdateService 负责「交付版本」：仅在用户确认后启动独立更新器。</item>
/// </list>
///
/// <para>
/// Debug 模式下从本地 http://127.0.0.1:9090/version.json 读取，可验证提示内容和
/// <c>availableAfter</c> 时间闸门。<c>forceUpdate</c> 为旧契约兼容字段，客户端始终忽略。
/// </para>
/// </summary>
public sealed class UpdateService
{
    public static UpdateService Instance { get; } = new();
    private UpdateService() { }

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Clearlove0923/SteamCN-GameLauncher/releases/latest";
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/Clearlove0923/SteamCN-GameLauncher/releases?per_page=20";
    private const string ReleasesPageUrl =
        "https://github.com/Clearlove0923/SteamCN-GameLauncher/releases";
    private const string CnbReleasesPageUrl =
        "https://cnb.cool/SteamCN-GameLauncher/SteamCN-GameLauncher/-/releases";
    private const string DebugLocalUrl = "http://127.0.0.1:9090/version.json";

    /// <summary>发现新版本时触发。正文来自 Release notes，用于展示新增功能和修复。</summary>
    public event Action<LauncherUpdateInfo>? UpdateAvailable;

    private LauncherUpdateInfo? _cachedUpdate;
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
    public async Task CheckUpdateAsync()
    {
        try
        {
            PendingGateUntil = null;
            LastCheckSucceeded = false;
            LastCheckError = null;

            var settings = LoadSettings();
            var debug = settings.DebugMode;
            var sourceId = UpdateSourcePolicy.Normalize(settings.UpdateSourceId);
            string remoteVersion;
            string message;
            string downloadUrl;

            if (debug)
            {
                var json = await FetchJsonAsync(DebugLocalUrl).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<VersionInfo>(json, _jsonOptions)
                    ?? throw new InvalidDataException("本地 version.json 无效。");
                remoteVersion = info.Version;
                message = info.Message;
                downloadUrl = string.IsNullOrWhiteSpace(info.DownloadUrl.Global)
                    ? info.DownloadUrl.Domestic : info.DownloadUrl.Global;

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
                var release = await GetLatestUpdateAsync(sourceId, settings.BetaChannel).ConfigureAwait(false);
                remoteVersion = release.Version;
                message = release.ReleaseNotes;
                downloadUrl = release.DownloadUrl;
            }

            LastCheckSucceeded = true;
            if (!ReleaseVersionComparer.IsNewer(remoteVersion, AppInfo.FullVersion)) return;

            _cachedUpdate = new LauncherUpdateInfo(
                remoteVersion,
                string.IsNullOrWhiteSpace(message) ? "本次发布未提供更新说明。" : message.Trim(),
                downloadUrl,
                sourceId);
            HasPendingUpdate = true;
            UpdateAvailable?.Invoke(_cachedUpdate);
        }
        catch (Exception ex)
        {
            // 启动阶段保持静默；手动检查通过 LastCheckSucceeded 显示明确的网络错误。
            LastCheckError = ex.Message;
            LogService.Instance.AddLog($"[更新] 版本检查失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    public void ReplayIfPending()
    {
        if (HasPendingUpdate && _cachedUpdate is not null)
            UpdateAvailable?.Invoke(_cachedUpdate);
    }

    /// <summary>
    /// 正式渠道使用 latest 端点（自动排除草稿与预发布）；测试渠道选择最新非草稿 Release。
    /// </summary>
    private static async Task<GitHubReleaseInfo> GetLatestGitHubReleaseAsync(bool includePrerelease)
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

    public async Task<LauncherUpdateInfo> GetLatestUpdateAsync(string? sourceId, bool includePrerelease)
    {
        sourceId = UpdateSourcePolicy.Normalize(sourceId);
#if DEBUG
        if (LoadSettings().DebugMode)
        {
            var json = await FetchJsonAsync(DebugLocalUrl).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<VersionInfo>(json, _jsonOptions)
                ?? throw new InvalidDataException("本地版本测试数据无效。");
            return new LauncherUpdateInfo(info.Version, info.Message, UpdateSourcePolicy.GetManualDownloadUrl(sourceId), sourceId);
        }
#endif
        var release = sourceId == UpdateSourceIds.Cnb
            ? await GetLatestCnbReleaseAsync(includePrerelease).ConfigureAwait(false)
            : await GetLatestGitHubReleaseAsync(includePrerelease).ConfigureAwait(false);
        return new LauncherUpdateInfo(release.TagName,
            string.IsNullOrWhiteSpace(release.Body) ? "本次发布未提供更新说明。" : release.Body.Trim(),
            UpdateSourcePolicy.GetManualDownloadUrl(sourceId), sourceId);
    }

    /// <summary>
    /// CNB 的 Release API 要求登录，客户端不能内置访问令牌；因此从公开 Release 页面读取
    /// 服务端渲染的版本号和发布说明。页面改版时异常会被启动期静默处理，不影响主程序使用。
    /// </summary>
    private static async Task<GitHubReleaseInfo> GetLatestCnbReleaseAsync(bool includePrerelease)
    {
        var html = await FetchStringAsync(CnbReleasesPageUrl, "text/html").ConfigureAwait(false);
        return CnbReleasePageParser.Parse(html, includePrerelease);
    }


    private static async Task<string> FetchJsonAsync(string url)
        => await FetchStringAsync(url, "application/vnd.github+json").ConfigureAwait(false);

    private static async Task<string> FetchStringAsync(string url, string accept)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (request.RequestUri?.Host == "api.github.com")
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SteamCN-GameLauncher/{AppInfo.Version.TrimStart('v')}");
        return client;
    }

}
