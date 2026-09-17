using System.Text.Json.Serialization;

namespace SteamCNGameLauncher.Models;

/// <summary>GitHub Releases REST API 中更新检查需要的最小字段集合。</summary>
public sealed class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = "";

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }
}
