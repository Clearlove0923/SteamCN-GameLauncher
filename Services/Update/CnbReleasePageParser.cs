using System.Text.Json;
using System.Text.RegularExpressions;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>CNB 公开页面的结构化 Release 数据解析；无登录依赖，页面结构不兼容时明确失败。</summary>
public static partial class CnbReleasePageParser
{
    public static GitHubReleaseInfo Parse(string html, bool includePrerelease)
    {
        // 只消费公开页面的结构化 Release 数据，不能从整个 HTML 猜测版本/拼接正文。
        var match = CnbPageDataRegex().Match(html);
        if (!match.Success) throw new InvalidDataException("CNB 公开发布页缺少版本数据，请稍后重试。");
        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        var releases = document.RootElement.GetProperty("props").GetProperty("pageProps")
            .GetProperty("initialState").GetProperty("slug").GetProperty("repo")
            .GetProperty("releases").GetProperty("list").GetProperty("data").GetProperty("releases");
        var candidates = new List<GitHubReleaseInfo>();
        foreach (var item in releases.EnumerateArray())
        {
            var tag = item.GetProperty("tag_ref").GetString() ?? "";
            if (!tag.StartsWith("refs/tags/", StringComparison.Ordinal)) continue;
            tag = tag[10..];
            candidates.Add(new GitHubReleaseInfo
            {
                TagName = tag,
                Name = item.GetProperty("title").GetString(),
                Body = item.GetProperty("body").GetString(),
                Draft = item.GetProperty("is_draft").GetBoolean(),
                Prerelease = item.GetProperty("is_prerelease").GetBoolean(),
                PublishedAt = item.GetProperty("published_at").TryGetDateTimeOffset(out var date) ? date : null,
                HtmlUrl = UpdateSourcePolicy.CnbRepositoryUrl + "/-/releases/tag/" + Uri.EscapeDataString(tag)
            });
        }
        return GitHubReleaseSelector.SelectLatest(candidates, includePrerelease)
            ?? throw new InvalidDataException("CNB 公开发布页没有可用版本。");
    }

    [GeneratedRegex("<script\\b[^>]*\\bid=[\"\']__NEXT_DATA__[\"\'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex CnbPageDataRegex();
}
