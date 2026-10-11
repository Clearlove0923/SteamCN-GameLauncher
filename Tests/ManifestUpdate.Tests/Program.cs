using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

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

var root = Path.Combine(Path.GetTempPath(), "manifest-update-" + Guid.NewGuid().ToString("N"));
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
    Check(service.CleanupBackups("123") == 1
        && Directory.GetFiles(Path.Combine(backups, "123"), "*.acf").Length == 1,
        "backups older than 30 days are removed");

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
