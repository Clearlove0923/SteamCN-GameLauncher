using System.Text.Json;
using SteamCNGameLauncher.Services;

var start = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(8));
var sessions = new List<PlaySession>();

Check(PlayTimeHeatLevelClassifier.Classify(TimeSpan.Zero) == PlayTimeHeatLevel.None,
    "zero duration keeps the inactive heatmap color");
Check(PlayTimeHeatLevelClassifier.Classify(TimeSpan.FromMinutes(59)) == PlayTimeHeatLevel.Short,
    "durations below one hour use the green heatmap level");
Check(PlayTimeHeatLevelClassifier.Classify(TimeSpan.FromHours(1)) == PlayTimeHeatLevel.Medium &&
      PlayTimeHeatLevelClassifier.Classify(TimeSpan.FromHours(3) - TimeSpan.FromTicks(1)) == PlayTimeHeatLevel.Medium,
    "durations from one hour to below three hours use the yellow heatmap level");
Check(PlayTimeHeatLevelClassifier.Classify(TimeSpan.FromHours(3)) == PlayTimeHeatLevel.Long,
    "durations from three hours use the red heatmap level");

// An externally launched game is first noticed after it started.
PlayTimeSessionReconciler.Observe(sessions, "game", 123, start, start.AddMinutes(17), "cn");
Check(sessions.Count == 1 && sessions[0].StartedAt == start &&
      sessions[0].EndedAt == start.AddMinutes(17), "external launch uses actual process start");

// Simulate disk serialization and app restart while the same process keeps running.
var json = JsonSerializer.Serialize(sessions);
sessions = JsonSerializer.Deserialize<List<PlaySession>>(json)!;
PlayTimeSessionReconciler.Observe(sessions, "game", 123, start, start.AddMinutes(42), "steam");
Check(sessions.Count == 1 && sessions[0].EndedAt == start.AddMinutes(42) &&
      sessions[0].LaunchCategory == "cn", "restart extends the same session and preserves category");

// A reused PID must not merge separate game launches.
PlayTimeSessionReconciler.Observe(sessions, "game", 123, start.AddHours(2), start.AddHours(2).AddMinutes(5), "steam");
Check(sessions.Count == 2 && sessions[1].LaunchCategory == "steam", "PID reuse creates a new session");

// Upgrade records from the first implementation without double counting.
var old = new List<PlaySession>
{
    new("game", start.AddMinutes(3), start.AddMinutes(9), "cn"),
    new("game", start.AddMinutes(15), start.AddMinutes(22), "cn")
};
PlayTimeSessionReconciler.Observe(old, "game", 456, start, start.AddMinutes(30), "steam");
Check(old.Count == 1 && old[0].StartedAt == start && old[0].EndedAt == start.AddMinutes(30) &&
      old[0].LaunchCategory == "cn", "legacy fragments join without duplicate duration");

var tempRoot = Path.Combine(Path.GetTempPath(), "game-time-test-" + Guid.NewGuid().ToString("N"));
try
{
    var gameTimeRoot = Path.Combine(tempRoot, "GameTime");
    var legacyPath = Path.Combine(tempRoot, "old", "play-time.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
    var firstGameId = Guid.NewGuid().ToString("N");
    var secondGameId = Guid.NewGuid().ToString("N");
    var folderNames = new Dictionary<string, string>
    {
        [firstGameId] = "Genshin Impact Game",
        [secondGameId] = "Second Game"
    };
    var oldSession = new PlaySession(firstGameId, start, start.AddMinutes(17), "cn", 123, start);
    File.WriteAllText(legacyPath, JsonSerializer.Serialize(new[] { oldSession }));

    var storage = new GameTimeStorage(gameTimeRoot, legacyPath);
    var imported = storage.Load(folderNames);
    Check(imported.SequenceEqual(new[] { oldSession }) && File.Exists(legacyPath),
        "legacy sessions migrate without deleting the original record");

    var secondSession = new PlaySession(secondGameId, start, start.AddMinutes(8), "steam");
    imported.Add(secondSession);
    storage.Save(imported, folderNames);
    var firstFile = Path.Combine(gameTimeRoot, "Genshin Impact Game", "Genshin Impact Game Timing.json");
    var secondFile = Path.Combine(gameTimeRoot, "Second Game", "Second Game Timing.json");
    Check(File.Exists(firstFile) && File.Exists(secondFile) &&
          JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(firstFile))!.Single() == oldSession &&
          JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(secondFile))!.Single() == secondSession,
        "matched game names are the install-directory record folders");

    var restarted = storage.Load(folderNames);
    Check(restarted.Count == 2 && restarted.Contains(oldSession) && restarted.Contains(secondSession),
        "restarting does not re-import and double-count legacy sessions");

    // Upgrade an installation that already wrote the previous ID-based layout.
    var previousFolder = Path.Combine(gameTimeRoot, firstGameId);
    Directory.CreateDirectory(previousFolder);
    File.WriteAllText(Path.Combine(previousFolder, "sessions.json"), JsonSerializer.Serialize(new[] { oldSession }));
    var migrated = storage.Load(folderNames);
    Check(migrated.Count == 2 && File.Exists(firstFile) &&
          File.Exists(Path.Combine(previousFolder, "sessions.json.migrated")),
        "ID-based records move to the matched-name folder without duplicate duration");

    var oldNamedFile = Path.Combine(gameTimeRoot, "Genshin Impact Game", "sessions.json");
    File.WriteAllText(oldNamedFile, JsonSerializer.Serialize(new[] { oldSession }));
    storage.Load(folderNames);
    Check(File.Exists(firstFile) && File.Exists(oldNamedFile + ".migrated"),
        "named folders upgrade sessions.json to the matched Timing filename");

    // Two presets for one game share the named game folder while retaining preset IDs in JSON.
    folderNames[secondGameId] = "Genshin Impact Game";
    storage.Load(folderNames);
    var shared = JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(firstFile))!;
    Check(shared.Count == 2 && shared.Contains(oldSession) && shared.Contains(secondSession),
        "multiple presets for one matched game share one record file");

    folderNames[secondGameId] = "Second Game";
    storage.Load(folderNames);
    Check(JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(firstFile))!.Count == 1 &&
          JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(secondFile))!.Single() == secondSession,
        "changed matches split a shared game file without dropping sessions");

    folderNames.Remove(secondGameId);
    storage.Load(folderNames);
    Check(File.Exists(secondFile) && !File.Exists(Path.Combine(gameTimeRoot, secondGameId, "sessions.json")),
        "removed presets retain their existing named record folder");
}
finally
{
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("16 play-time checks passed.");

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
