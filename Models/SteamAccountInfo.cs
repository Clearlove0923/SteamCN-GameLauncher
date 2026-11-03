namespace SteamCNGameLauncher.Models;

/// <summary>An account recorded by Steam in config/loginusers.vdf.</summary>
public sealed record SteamAccountInfo(
    string SteamId,
    string AccountName,
    string PersonaName,
    bool IsMostRecent,
    long Timestamp)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(PersonaName)
        ? PersonaName
        : !string.IsNullOrWhiteSpace(AccountName) ? AccountName : SteamId;

    public string DisplayText => $"{DisplayName} · {SteamId}" + (IsMostRecent ? " · 最近登录" : "");
}
