using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCNGameLauncher.Models.Home;

/// <summary>发送给 Python 聚合层的不可变首页请求。</summary>
public sealed record HomeContentRequest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("gameId")]
    public required string GameId { get; init; }

    [JsonPropertyName("providerId")]
    public required string ProviderId { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "zh-CN";

    [JsonPropertyName("providerOptions")]
    public IReadOnlyDictionary<string, JsonElement> ProviderOptions { get; init; }
        = new Dictionary<string, JsonElement>();
}

/// <summary>Python 返回的带版本首页信封。</summary>
public sealed record HomeContentEnvelope
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = "";

    [JsonPropertyName("providerId")]
    public string ProviderId { get; init; } = "";

    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset FetchedAt { get; init; }

    [JsonPropertyName("content")]
    public HomeContent Content { get; init; } = new();

    [JsonPropertyName("errors")]
    public IReadOnlyList<HomeContentError> Errors { get; init; } = Array.Empty<HomeContentError>();
}

/// <summary>与厂商无关的首页内容，只包含背景、Banner、资讯与更新信息。</summary>
public sealed record HomeContent
{
    [JsonPropertyName("background")]
    public HomeBackground? Background { get; init; }

    [JsonPropertyName("banners")]
    public IReadOnlyList<HomeBanner> Banners { get; init; } = Array.Empty<HomeBanner>();

    [JsonPropertyName("news")]
    public IReadOnlyList<HomeNewsItem> News { get; init; } = Array.Empty<HomeNewsItem>();

    [JsonPropertyName("updateInfo")]
    public HomeUpdateInfo? UpdateInfo { get; init; }
}

public sealed record HomeBackground
{
    [JsonPropertyName("videoUrl")]
    public string? VideoUrl { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("localPath")]
    public string? LocalPath { get; init; }
}

public sealed record HomeBanner
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("localPath")]
    public string? LocalPath { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; init; }

    [JsonPropertyName("startsAt")]
    public DateTimeOffset? StartsAt { get; init; }

    [JsonPropertyName("endsAt")]
    public DateTimeOffset? EndsAt { get; init; }
}

public sealed record HomeNewsItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed record HomeUpdateInfo
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed record HomeContentError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("recoverable")]
    public bool Recoverable { get; init; } = true;
}

public sealed record HomeContentResult(
    HomeContent Content,
    bool IsStale = false,
    IReadOnlyList<HomeContentError>? Errors = null);
