using SteamCNGameLauncher.Services;

var failures = 0;
void Check(bool condition, string name)
{
    if (condition) Console.WriteLine($"PASS: {name}");
    else { Console.WriteLine($"FAIL: {name}"); failures++; }
}

const string sample = """
"users"
{
    "76561198000000001"
    {
        "AccountName" "first_login"
        "PersonaName" "第一位玩家"
        "MostRecent" "0"
        "Timestamp" "100"
    }
    "76561198000000002"
    {
        "AccountName" "recent_login"
        "PersonaName" "Recent \"Player\""
        "MostRecent" "1"
        "Timestamp" "200"
    }
    "not-a-steam-id" { "MostRecent" "1" }
}
""";

var parsed = SteamAccountService.Parse(sample);
Check(parsed.Count == 2, "parser returns only valid SteamID64 accounts");
Check(parsed[1].IsMostRecent && parsed[1].PersonaName == "Recent \"Player\"", "parser reads recent flag and escaped persona name");

var root = Path.Combine(Path.GetTempPath(), $"SteamAccountTests-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(Path.Combine(root, "config"));
    File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), sample);
    var result = new SteamAccountService().Discover(Path.Combine(root, "steam.exe"));
    Check(result.IsSuccess && result.Accounts[0].SteamId == "76561198000000002", "discovery accepts steam.exe path and sorts MostRecent first");
    Check(result.Accounts[1].SteamId == "76561198000000001", "discovery preserves all accounts for user selection");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

var missing = new SteamAccountService().Discover(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));
Check(!missing.IsSuccess && missing.Error.Contains("loginusers.vdf"), "missing config returns an actionable error");

return failures == 0 ? 0 : 1;
