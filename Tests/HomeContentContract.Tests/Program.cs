using System.Text.Json;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services.Home;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

var samplePath = Path.Combine(AppContext.BaseDirectory, "home-content-v1.json");
var envelope = HomeContentJson.DeserializeEnvelope(File.ReadAllText(samplePath));
Check(envelope.SchemaVersion == 1 && envelope.ProviderId == "hoyoplay-json",
    "Python envelope metadata deserializes");
Check(envelope.Content.Background?.VideoUrl?.EndsWith(".mp4", StringComparison.Ordinal) == true,
    "background animation URL deserializes");
Check(envelope.Content.Banners.Single().TargetUrl == "https://example.invalid/activity/1"
      && envelope.Content.News.Single().TargetUrl == "https://example.invalid/news/1",
    "banner and news links deserialize");
Check(envelope.Content.GetType().GetProperty("ScreenshotPath") == null,
    "HomeContent remains limited to the unified homepage model");

var screenshotEnvelope = HomeContentJson.DeserializeScreenshotPathEnvelope(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-screenshot-path-v1.json")));
Check(screenshotEnvelope.GameId == "4162040"
      && screenshotEnvelope.ScreenshotPath?.Contains("4162040", StringComparison.Ordinal) == true,
    "separate Python-owned screenshot path deserializes");

var requestJson = HomeContentJson.SerializeRequest(new HomeContentRequest
{
    RequestId = "request-1",
    GameId = "4162040",
    ProviderId = "hoyoplay-json"
});
using var requestDocument = JsonDocument.Parse(requestJson);
Check(requestDocument.RootElement.GetProperty("schemaVersion").GetInt32() == 1
      && requestDocument.RootElement.GetProperty("gameId").GetString() == "4162040"
      && requestDocument.RootElement.GetProperty("providerId").GetString() == "hoyoplay-json",
    "C# request uses the camelCase Python contract");

try
{
    HomeContentJson.DeserializeEnvelope("{\"schemaVersion\":2,\"providerId\":\"test\",\"content\":{}}");
    throw new Exception("FAILED: unsupported schema version rejected");
}
catch (JsonException)
{
    checks++;
    Console.WriteLine("PASS: unsupported schema version rejected");
}

Console.WriteLine($"All {checks} checks passed.");
