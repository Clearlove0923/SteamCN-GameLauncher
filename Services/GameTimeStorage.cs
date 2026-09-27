using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamCNGameLauncher.Services;

/// <summary>Stores sessions under the same matched game folder name used by the home page.</summary>
public sealed class GameTimeStorage
{
    private const string LegacySessionFileName = "sessions.json";
    private const string MigrationMarker = "legacy-import.complete";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root;
    private readonly string _legacyPath;
    private readonly Dictionary<string, string> _historicFolderNames = new(StringComparer.Ordinal);

    public GameTimeStorage(string root, string legacyPath)
    {
        _root = root;
        _legacyPath = legacyPath;
    }

    public List<PlaySession> Load(IReadOnlyDictionary<string, string> folderNames)
    {
        Directory.CreateDirectory(_root);
        var sessions = new List<PlaySession>();
        var existingFiles = Directory.EnumerateFiles(_root, LegacySessionFileName, SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_root, "* Timing.json", SearchOption.AllDirectories))
            .ToArray();
        var layoutChanged = false;
        foreach (var file in existingFiles)
        {
            var fileSessions = JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(file)) ?? [];
            var existingFolder = Path.GetFileName(Path.GetDirectoryName(file)!);
            foreach (var session in fileSessions)
                if (!folderNames.ContainsKey(session.PresetId))
                    _historicFolderNames.TryAdd(session.PresetId, existingFolder);
            foreach (var session in fileSessions)
            {
                if (!string.Equals(file, GetSessionPath(session.PresetId, folderNames),
                        StringComparison.OrdinalIgnoreCase))
                    layoutChanged = true;
                Merge(sessions, session);
            }
        }

        var marker = Path.Combine(_root, MigrationMarker);
        var importLegacy = !File.Exists(marker) && File.Exists(_legacyPath);
        if (importLegacy)
        {
            var legacy = JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(_legacyPath)) ?? [];
            foreach (var item in legacy) Merge(sessions, item);
        }

        var targetFiles = sessions.Where(x => !string.IsNullOrWhiteSpace(x.PresetId))
            .Select(x => GetSessionPath(x.PresetId, folderNames))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (importLegacy || layoutChanged || existingFiles.Any(file => !targetFiles.Contains(file)))
        {
            Save(sessions, folderNames);
            // Preserve old ID-based files as backups after the named files have been written.
            foreach (var file in existingFiles.Where(file => !targetFiles.Contains(file)))
            {
                var backup = file + ".migrated";
                if (File.Exists(backup)) backup += "." + Guid.NewGuid().ToString("N");
                File.Move(file, backup);
            }
        }
        if (importLegacy)
        {
            // Keep the original AppData file as a backup; the marker prevents re-import.
            File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
        }
        return sessions;
    }

    public void Save(IEnumerable<PlaySession> sessions, IReadOnlyDictionary<string, string> folderNames)
    {
        Directory.CreateDirectory(_root);
        foreach (var group in sessions.Where(x => !string.IsNullOrWhiteSpace(x.PresetId))
                     .GroupBy(x => GetSessionPath(x.PresetId, folderNames), StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetDirectoryName(group.Key)!;
            Directory.CreateDirectory(folder);
            var path = group.Key;
            var temp = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(group.ToArray(), JsonOptions));
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    private string GetSessionPath(string presetId, IReadOnlyDictionary<string, string> folderNames)
    {
        var name = folderNames.TryGetValue(presetId, out var configured) ? configured
            : _historicFolderNames.TryGetValue(presetId, out var historic) ? historic : SafePresetId(presetId);
        var safe = new string(name.Trim().Where(ch => !Path.GetInvalidFileNameChars().Contains(ch) &&
            !char.IsControl(ch)).Take(80).ToArray()).Trim().TrimEnd(' ', '.');
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" };
        if (string.IsNullOrWhiteSpace(safe) || reserved.Contains(safe.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            safe = SafePresetId(presetId);
        return Path.Combine(_root, safe, $"{safe} Timing.json");
    }

    private static string SafePresetId(string id) => Guid.TryParse(id, out var guid)
        ? guid.ToString("N")
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();

    private static void Merge(List<PlaySession> sessions, PlaySession candidate)
    {
        var index = sessions.FindIndex(x => x.PresetId == candidate.PresetId &&
            (candidate.ProcessId is not null && x.ProcessId == candidate.ProcessId &&
             x.ProcessStartedAt == candidate.ProcessStartedAt || x == candidate));
        if (index >= 0)
        {
            if (candidate.EndedAt > sessions[index].EndedAt)
                sessions[index] = sessions[index] with { EndedAt = candidate.EndedAt };
            return;
        }
        if (candidate.ProcessId is null && sessions.Any(x => x.PresetId == candidate.PresetId &&
                x.ProcessId is not null && x.StartedAt <= candidate.StartedAt && x.EndedAt >= candidate.EndedAt))
            return;
        sessions.Add(candidate);
    }
}
