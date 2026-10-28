using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

if (args.Length == 2)
{
    var inspected = await SteamAppCacheReader.ReadAsync(args[0], args[1]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(inspected));
    return;
}

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

void Reject(string content, string appId, string build, string message)
{
    try { SteamManifestUpdateGuard.Validate(content, appId, build); }
    catch (SteamAppInfoException) { Check(true, message); return; }
    throw new Exception("FAILED: " + message);
}
var pending = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nte-update-pending.acf"));
await CacheTests.Run(Check, pending);
Reject(pending, "4706890", "26092471", "NTE stale API result is rejected against Steam target");
Reject(pending, "4706890", "26236513", "different target is a conflict even with a higher candidate ID");
Reject(pending, "123", "26236512", "wrong AppID is rejected");
SteamManifestUpdateGuard.Validate(pending, "4706890", "26236512");
Check(true, "matching target passes version comparison");
var noTarget = pending.Replace("\"TargetBuildID\" \"26236512\"", "\"TargetBuildID\" \"0\"");
SteamManifestUpdateGuard.Validate(noTarget, "4706890", "26092471");
Check(true, "zero target is not a conflict");
Reject(noTarget, "4706890", "26092470", "automatic rollback is rejected");
Reject(pending[..^3], "4706890", "26236512", "truncated ACF fails closed");
Reject(pending.Replace("\"StateFlags\" \"6\"", "\"buildid\" \"1\""), "4706890", "26236512", "duplicate fields fail closed");
Reject(pending.Replace("\"26236512\"", "\"unknown\""), "4706890", "26236512", "invalid target fails closed");
SteamManifestUpdateGuard.Validate("// comment\n\"AppState\" { \"appid\" \"123\" \"BUILDID\" \"10\" \"Nested\" { \"TargetBuildID\" \"999\" } }", "123", "10");
Check(true, "nested fields are ignored and field casing and comments are supported");

SteamDepotCandidate Depot(string id, string manifest) => new(
    new SteamDepotInfo(id, id, new("windows", "64", "schinese"), "0",
        new Dictionary<string, string> { ["public"] = manifest }), manifest);
SteamExecutableCandidate Exe(string path) => new(path,
    new[] { new SteamLaunchInfo("0", "", path, "", "default", new("windows", "64", "")) });
SteamAppSelection Selection(IReadOnlyList<SteamDepotCandidate> depots,
    IReadOnlyList<SteamExecutableCandidate>? executables = null) =>
    new("Game", "Game", "10", depots, executables ?? new[] { Exe("Game.exe") }, Array.Empty<string>());

var allCompatible = SteamAppSelectionService.ChooseDepotsForAutomaticUpdate(
    Selection(new[] { Depot("1", "11"), Depot("2", "22") }));
Check(allCompatible.Candidates.Select(candidate => candidate.Depot.Id).SequenceEqual(new[] { "1", "2" }),
    "all compatible depots with manifests are selected in source order");
var soleManifest = SteamAppSelectionService.ChooseDepotsForAutomaticUpdate(
    Selection(new[] { Depot("1", ""), Depot("2", "22") }));
Check(soleManifest.Candidates.Single().Depot.Id == "2", "depots without manifests are excluded automatically");
var noManifest = SteamAppSelectionService.ChooseDepotsForAutomaticUpdate(
    Selection(new[] { Depot("1", ""), Depot("2", "") }));
Check(!noManifest.IsSuccess && noManifest.Error.Contains("没有找到"), "depots without manifests cannot be selected");
Check(SteamAppSelectionService.ChooseExecutableForAutomaticUpdate(
    Selection(new[] { Depot("1", "11") }, new[] { Exe("A.exe"), Exe("B.exe") }), "b.EXE").Candidate?.Path == "B.exe",
    "current executable is matched case-insensitively");

var acf = AcfContentBuilder.Build(new AcfParameters(
    "123",
    new[]
    {
        new SteamDepotConfig { DepotId = "1", ManifestId = "11" },
        new SteamDepotConfig { DepotId = "2", ManifestId = "22", Size = "33" },
    },
    @"C:\\Steam\\steam.exe", "Game", "Game", "10", "76561198000000000"));
var depotOneHeader = "\t\t\"1\"" + Environment.NewLine + "\t\t{";
var depotTwoHeader = "\t\t\"2\"" + Environment.NewLine + "\t\t{";
Check(acf.Contains("\"manifest\"\t\t\"11\"") && acf.Contains("\"manifest\"\t\t\"22\"")
    && acf.IndexOf(depotOneHeader, StringComparison.Ordinal) >= 0
    && acf.IndexOf(depotOneHeader, StringComparison.Ordinal) < acf.IndexOf(depotTwoHeader, StringComparison.Ordinal),
    "ACF writes every depot with its own manifest in source order");
try
{
    AcfContentBuilder.Build(new AcfParameters("123", Array.Empty<SteamDepotConfig>(), "steam.exe",
        "Game", "Game", "10", "1"));
    throw new Exception("FAILED: empty depot set accepted");
}
catch (ArgumentException) { Check(true, "ACF rejects an empty depot set"); }

var root = Path.Combine(AppContext.BaseDirectory, "manifest-update-" + Guid.NewGuid().ToString("N"));
var target = Path.Combine(root, "library", "steamapps", "appmanifest_123.acf");
var backups = Path.Combine(root, "backups");
var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
Check(ManifestFileService.DefaultBackupRoot == Path.Combine(AppContext.BaseDirectory, "backups"),
    "default backup root is the application installation directory");
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    await File.WriteAllTextAsync(target, "version-0");
    var service = new ManifestFileService(backups, ManifestBackupOptions.Default, () => now);
    var conflictTarget = Path.Combine(Path.GetDirectoryName(target)!, "appmanifest_4706890.acf");
    await File.WriteAllTextAsync(conflictTarget, pending);
    try
    {
        await service.WriteWithBackupAsync(conflictTarget, "must not be written", "4706890", "26092471",
            candidateBuildId: "26092471");
        throw new Exception("FAILED: conflicting write accepted");
    }
    catch (SteamAppInfoException)
    {
        Check(await File.ReadAllTextAsync(conflictTarget) == pending && !Directory.Exists(backups),
            "conflicting write preserves original bytes and creates no backups");
    }
    for (var version = 1; version <= 3; version++)
    {
        now = now.AddMinutes(1);
        var result = await service.WriteWithBackupAsync(target, $"version-{version}", "123", (version - 1).ToString());
        Check(result.Changed && result.BackupPath != null, $"update {version} creates a backup before replacement");
    }
    var retained = Directory.GetFiles(Path.Combine(backups, "123"), "*.acf");
    Check(retained.Length == 2, "only the two newest backups are retained per AppID");
    Check(retained.Select(File.ReadAllText).Order().SequenceEqual(new[] { "version-1", "version-2" }),
        "the retained files are the two latest previous versions");

    var identical = await service.WriteWithBackupAsync(target, "version-3", "123", "3");
    Check(!identical.Changed && identical.BackupPath == null
        && Directory.GetFiles(Path.Combine(backups, "123"), "*.acf").Length == 2,
        "identical content creates no backup and no write");

    var stale = retained[0];
    File.SetLastWriteTimeUtc(stale, now.AddDays(-31).UtcDateTime);
    var withinRetention = await service.WriteWithBackupAsync(target, "version-3", "123", "3");
    Check(withinRetention.RemovedBackupCount == 0 && File.Exists(stale),
        "31-day-old backup survives an unchanged update");
    File.SetLastWriteTimeUtc(stale, now.AddDays(-60).UtcDateTime);
    Check(service.CleanupBackups("123") == 0 && File.Exists(stale),
        "backup exactly 60 days old is retained");
    File.SetLastWriteTimeUtc(stale, now.AddDays(-61).UtcDateTime);
    var expiredUpdate = await service.WriteWithBackupAsync(target, "version-3", "123", "3");
    Check(!expiredUpdate.Changed && expiredUpdate.RemovedBackupCount == 1
        && Directory.GetFiles(Path.Combine(backups, "123"), "*.acf").Length == 1,
        "unchanged update automatically removes backups older than 60 days");

    var otherTarget = Path.Combine(root, "library", "steamapps", "appmanifest_456.acf");
    await File.WriteAllTextAsync(otherTarget, "other-0");
    now = now.AddMinutes(1);
    await service.WriteWithBackupAsync(otherTarget, "other-1", "456", "0");
    Check(Directory.GetFiles(Path.Combine(backups, "456"), "*.acf").Length == 1,
        "backup retention is isolated per AppID");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

Console.WriteLine($"All {checks} checks passed.");
