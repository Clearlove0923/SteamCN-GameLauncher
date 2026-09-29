namespace SteamCNGameLauncher.Models;

public static class UpdateSourceIds
{
    public const string Cnb = "cnb";
    public const string GitHub = "github";

    public static bool IsSupported(string? value) => value is Cnb or GitHub;

    public static string Normalize(string? value) => IsSupported(value) ? value! : Cnb;
}
