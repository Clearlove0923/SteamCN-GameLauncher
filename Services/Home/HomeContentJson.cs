using System.Text.Json;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>统一首页 JSON 的唯一序列化入口，避免各页面采用不同选项。</summary>
public static class HomeContentJson
{
    public const int SupportedSchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public static HomeContentEnvelope DeserializeEnvelope(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var envelope = JsonSerializer.Deserialize<HomeContentEnvelope>(json, Options)
            ?? throw new JsonException("Python 首页响应为空。");
        if (envelope.SchemaVersion != SupportedSchemaVersion)
            throw new JsonException($"不支持的首页 schemaVersion：{envelope.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(envelope.ProviderId))
            throw new JsonException("Python 首页响应缺少 providerId。");
        return envelope;
    }

    public static string SerializeRequest(HomeContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, Options);
    }

    public static GameScreenshotPathEnvelope DeserializeScreenshotPathEnvelope(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var envelope = JsonSerializer.Deserialize<GameScreenshotPathEnvelope>(json, Options)
            ?? throw new JsonException("Python 截图路径响应为空。");
        if (envelope.SchemaVersion != SupportedSchemaVersion)
            throw new JsonException($"不支持的截图路径 schemaVersion：{envelope.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(envelope.GameId))
            throw new JsonException("Python 截图路径响应缺少 gameId。");
        return envelope;
    }
}
