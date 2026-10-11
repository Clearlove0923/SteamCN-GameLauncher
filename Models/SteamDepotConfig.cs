namespace SteamCNGameLauncher.Models;

/// <summary>One installed Depot entry written to a Steam appmanifest.</summary>
public sealed class SteamDepotConfig
{
    public string DepotId { get; set; } = "";
    public string ManifestId { get; set; } = "";
    public string Size { get; set; } = "0";

    public string DisplayText => $"{DepotId} · Manifest {ManifestId}";

    public SteamDepotConfig Clone() => new()
    {
        DepotId = DepotId,
        ManifestId = ManifestId,
        Size = Size,
    };
}
