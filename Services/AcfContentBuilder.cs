using System.Globalization;
using System.Text;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

/// <summary>All values needed to create one Steam appmanifest.</summary>
public sealed record AcfParameters(
    string AppId,
    IReadOnlyList<SteamDepotConfig> Depots,
    string LauncherPath,
    string DisplayName,
    string InstallDir,
    string BuildId,
    string LastOwner,
    string Language = "schinese");

/// <summary>Pure ACF renderer shared by the UI and dependency-free regression tests.</summary>
public static class AcfContentBuilder
{
    public static string Build(AcfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Depots == null || parameters.Depots.Count == 0)
            throw new ArgumentException("At least one Depot is required.", nameof(parameters));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var depotText = new StringBuilder();
        foreach (var depot in parameters.Depots)
        {
            if (!IsPositiveId(depot.DepotId) || !IsPositiveId(depot.ManifestId))
                throw new ArgumentException("Every Depot must have a valid DepotID and ManifestID.", nameof(parameters));
            if (!seen.Add(depot.DepotId))
                throw new ArgumentException($"Duplicate DepotID: {depot.DepotId}", nameof(parameters));

            var size = ulong.TryParse(depot.Size, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize)
                ? parsedSize.ToString(CultureInfo.InvariantCulture)
                : "0";
            depotText.AppendLine($"\t\t\"{depot.DepotId}\"");
            depotText.AppendLine("\t\t{");
            depotText.AppendLine($"\t\t\t\"manifest\"\t\t\"{depot.ManifestId}\"");
            depotText.AppendLine($"\t\t\t\"size\"\t\t\"{size}\"");
            depotText.AppendLine("\t\t}");
        }

        var escapedLauncherPath = parameters.LauncherPath.Replace("\\", "\\\\");
        return $@"""AppState""
{{
	""appid""		""{parameters.AppId}""
	""universe""		""1""
	""LauncherPath""		""{escapedLauncherPath}""
	""name""		""{parameters.DisplayName}""
	""StateFlags""		""4""
	""installdir""		""{parameters.InstallDir}""
	""LastUpdated""		""0""
	""LastPlayed""		""0""
	""SizeOnDisk""		""0""
	""StagingSize""		""0""
	""buildid""		""{parameters.BuildId}""
	""LastOwner""		""{parameters.LastOwner}""
	""DownloadType""		""1""
	""UpdateResult""		""0""
	""BytesToDownload""		""0""
	""BytesDownloaded""		""0""
	""BytesToStage""		""0""
	""BytesStaged""		""0""
	""TargetBuildID""		""0""
	""AutoUpdateBehavior""		""1""
	""AllowOtherDownloadsWhileRunning""		""0""
	""ScheduledAutoUpdate""		""0""
	""InstalledDepots""
	{{
{depotText.ToString().TrimEnd()}
	}}
	""SharedDepots""
	{{
		""228989""		""228980""
	}}
	""UserConfig""
	{{
		""language""		""{parameters.Language}""
	}}
	""MountedConfig""
	{{
		""language""		""{parameters.Language}""
	}}
}}
";
    }

    private static bool IsPositiveId(string value) => ulong.TryParse(value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}
