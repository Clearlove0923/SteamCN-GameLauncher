using System.Diagnostics;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services;

/// <summary>Records observed game processes. A persisted end timestamp is also a crash-safe heartbeat.</summary>
public sealed class PlayTimeService : IDisposable
{
    public static PlayTimeService Instance { get; } = new();

    private readonly object _sync = new();
    private readonly GameTimeStorage _storage = new(
        Path.Combine(AppContext.BaseDirectory, "GameTime"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamCN-GameLauncher", "play-time.json"));
    private readonly List<PlaySession> _sessions;
    private readonly bool _storageLoaded;
    private IReadOnlyDictionary<string, string> _folderNames = new Dictionary<string, string>();
    private readonly Dictionary<string, (string Category, DateTimeOffset Requested)> _launchIntents = [];
    private CancellationTokenSource? _cancellation;

    private PlayTimeService()
    {
        try
        {
            _folderNames = GetFolderNames(new SettingsService().Load().CustomManifestPresets);
            _sessions = _storage.Load(_folderNames);
            _storageLoaded = true;
        }
        catch (Exception ex)
        {
            _sessions = [];
            LogService.Instance.AddLog($"[游戏时长] 读取记录失败：{ex.Message}");
        }
    }

    public void Start()
    {
        if (_cancellation is not null) return;
        _cancellation = new CancellationTokenSource();
        _ = MonitorAsync(_cancellation.Token);
    }

    public IReadOnlyList<PlaySession> GetSessions(string presetId)
    {
        lock (_sync)
            return _sessions.Where(x => x.PresetId == presetId && x.EndedAt > x.StartedAt).ToArray();
    }

    public void NoteLaunch(string presetId, string launchModeId)
    {
        lock (_sync)
            _launchIntents[presetId] = (launchModeId == HomeLaunchModeIds.DirectCn ? "cn" : "steam", DateTimeOffset.Now);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try { do
        {
            try { Scan(); }
            catch (Exception ex) { LogService.Instance.AddLog($"[游戏时长] 扫描失败：{ex.Message}"); }
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) { }
    }

    private void Scan()
    {
        var presets = new SettingsService().Load().CustomManifestPresets
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.ClientExePath))
            .ToArray();
        var now = DateTimeOffset.Now;
        using var currentProcess = Process.GetCurrentProcess();
        var sessionId = currentProcess.SessionId;
        lock (_sync)
        {
            _folderNames = GetFolderNames(presets);
            foreach (var preset in presets)
            {
                string target;
                try { target = Path.GetFullPath(preset.ClientExePath.Trim()); }
                catch { continue; }
                var processName = Path.GetFileNameWithoutExtension(target);
                var uniqueName = presets.Count(x =>
                    string.Equals(Path.GetFileNameWithoutExtension(x.ClientExePath.Trim()), processName,
                        StringComparison.OrdinalIgnoreCase)) == 1;
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            if (process.HasExited || process.SessionId != sessionId) continue;
                            try
                            {
                                if (!string.Equals(process.MainModule?.FileName, target,
                                        StringComparison.OrdinalIgnoreCase)) continue;
                            }
                            catch (System.ComponentModel.Win32Exception) when (uniqueName)
                            {
                                // Some elevated games deny MainModule access. A unique configured EXE name
                                // within this user's session is the remaining usable signal.
                            }
                            var processStart = new DateTimeOffset(process.StartTime);
                            var category = _launchIntents.TryGetValue(preset.Id, out var intent) &&
                                now - intent.Requested < TimeSpan.FromMinutes(5)
                                ? intent.Category
                                : preset.HomeLaunchModeId == HomeLaunchModeIds.DirectCn ? "cn" : "steam";
                            PlayTimeSessionReconciler.Observe(_sessions, preset.Id, process.Id,
                                processStart, now, category);
                        }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            Save();
        }
    }

    private void Save()
    {
        // A failed read must not replace an existing record with an empty scan.
        if (!_storageLoaded) return;
        try
        {
            _storage.Save(_sessions, _folderNames);
        }
        catch (Exception ex) { LogService.Instance.AddLog($"[游戏时长] 保存记录失败：{ex.Message}"); }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        lock (_sync) Save();
    }

    private static IReadOnlyDictionary<string, string> GetFolderNames(IEnumerable<CustomManifestPreset> presets)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var preset in presets.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
        {
            if (SupportedGameRegistry.TryMatch(preset.ClientExePath, preset.InstallDir, out var match))
            {
                var folder = SupportedGameRegistry.GetCacheFolderName(match, preset.ClientExePath, preset.InstallDir);
                names[preset.Id] = match.FolderNames.Contains(folder, StringComparer.OrdinalIgnoreCase)
                    ? folder : match.FolderNames.FirstOrDefault() ?? match.GameId;
            }
            else
            {
                names[preset.Id] = !string.IsNullOrWhiteSpace(preset.GameDisplayName)
                    ? preset.GameDisplayName : preset.Name;
            }
        }
        return names;
    }
}
