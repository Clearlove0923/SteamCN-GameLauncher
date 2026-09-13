using System.Text.Json.Serialization;

namespace SteamCNGameLauncher.Models.Home;

/// <summary>向 Python 截图路径注册表查询单个游戏。</summary>
public sealed record GameScreenshotPathRequest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("gameId")]
    public required string GameId { get; init; }
}

/// <summary>独立于 HomeContent 的截图路径响应。</summary>
public sealed record GameScreenshotPathEnvelope
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = "";

    [JsonPropertyName("gameId")]
    public string GameId { get; init; } = "";

    /// <summary>本机绝对目录，或包含 steamInstallPath/steamId 变量的路径模板。</summary>
    [JsonPropertyName("screenshotPath")]
    public string? ScreenshotPath { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<HomeContentError> Errors { get; init; } = Array.Empty<HomeContentError>();
}
