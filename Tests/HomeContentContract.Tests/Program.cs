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
Check(envelope.Content.Background?.Variants.Select(item => item.Id).SequenceEqual(
        ["animation-a", "animation-b"]) == true,
    "Python video variants deserialize without losing their stable IDs");
var pythonVariants = HomeContentJson.DeserializeEnvelope(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "hoyoplay-video-variants-envelope.json")));
Check(pythonVariants.Content.Background?.Variants.Select(item => item.Id).SequenceEqual(
        ["3citmgCMOP", "d7eCRqQwNc"]) == true
      && pythonVariants.Content.Background?.Variants.All(item => item.VideoUrl.EndsWith(".webm")) == true,
    "actual Python Pydantic output deserializes as two video-only Genshin variants");
var backdrop = HomeBackdropCoordinator.Instance;
HomeBackdropState? publishedBackdrop = null;
void OnBackdropChanged(HomeBackdropState state) => publishedBackdrop = state;
backdrop.Changed += OnBackdropChanged;
var firstOwner = Guid.NewGuid();
var secondOwner = Guid.NewGuid();
backdrop.Show(firstOwner, envelope.Content.Background, playAnimation: true);
Check(publishedBackdrop is { IsActive: true, PlayAnimation: true, ForceBlack: false }
      && publishedBackdrop.Background?.VideoUrl == envelope.Content.Background?.VideoUrl,
    "home page publishes source-agnostic background state to the window layer");
backdrop.Hold(firstOwner);
Check(publishedBackdrop is { IsHolding: true, IsActive: true }
      && publishedBackdrop.Background?.VideoUrl == envelope.Content.Background?.VideoUrl,
    "game switch holds the current backdrop until the next game is ready");
backdrop.Show(firstOwner, null, playAnimation: false);
Check(publishedBackdrop is { IsHolding: false, IsActive: true, Background: null },
    "a game without adapted media releases the hold and selects the appearance fallback");
backdrop.Show(secondOwner, envelope.Content.Background, playAnimation: true);
backdrop.Clear(firstOwner);
Check(publishedBackdrop is { IsActive: true },
    "late unload from the previous home page cannot clear the newly selected game");
backdrop.Clear(secondOwner);
Check(publishedBackdrop == HomeBackdropState.Inactive,
    "leaving home clears window-level playback state");
backdrop.Changed -= OnBackdropChanged;
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
    ProviderId = "auto",
    ExecutablePath = @"D:\Games\ZenlessZoneZero Game\ZenlessZoneZero.exe",
    InstallDirectory = @"D:\Games\ZenlessZoneZero Game",
    CacheFolderName = "ZenlessZoneZero Game",
});
using var requestDocument = JsonDocument.Parse(requestJson);
Check(requestDocument.RootElement.GetProperty("schemaVersion").GetInt32() == 1
      && requestDocument.RootElement.GetProperty("gameId").GetString() == "4162040"
      && requestDocument.RootElement.GetProperty("providerId").GetString() == "auto"
      && requestDocument.RootElement.GetProperty("executablePath").GetString()?.EndsWith("ZenlessZoneZero.exe") == true
      && requestDocument.RootElement.GetProperty("installDirectory").GetString()?.EndsWith("ZenlessZoneZero Game") == true
      && requestDocument.RootElement.GetProperty("cacheFolderName").GetString() == "ZenlessZoneZero Game",
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
