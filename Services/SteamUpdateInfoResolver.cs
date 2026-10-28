using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

public sealed record SteamResolvedUpdate(SteamAppInfo App, string Source, IReadOnlyList<string> Diagnostics);

/// <summary>Select a complete source snapshot consistent with local Steam version evidence.</summary>
public sealed class SteamUpdateInfoResolver(ISteamAppInfoService remote)
{
    public async Task<SteamResolvedUpdate> ResolveAsync(string appId, string cachePath, string manifestPath,
        SteamAppSelectionOptions options, CancellationToken token = default)
    {
        var diagnostics = new List<string>();
        var candidates = new List<(SteamAppInfo App, string Source)>();
        try { candidates.Add((await SteamAppCacheReader.ReadAsync(cachePath, appId, token), "本机 Steam 缓存")); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or SteamAppInfoException)
        { diagnostics.Add("本机 Steam 缓存不可用：" + ex.Message); }
        try
        {
            var app = await remote.GetAppInfoAsync(appId, token);
            candidates.Insert(0, (app, string.IsNullOrWhiteSpace(app.Source) ? "游戏信息接口" : app.Source));
        }
        catch (SteamAppInfoException ex) { diagnostics.Add("游戏信息接口不可用：" + ex.Message); }
        token.ThrowIfCancellationRequested();
        var acf = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath, token) : null;
        return Choose(candidates, acf, appId, options, diagnostics);
    }

    public static SteamResolvedUpdate Choose(IEnumerable<(SteamAppInfo App, string Source)> candidates,
        string? acf, string appId, SteamAppSelectionOptions options, List<string>? diagnostics = null)
    {
        diagnostics ??= new();
        var valid = new List<(SteamAppInfo App, string Source, ulong Build)>();
        foreach (var candidate in candidates)
        {
            try
            {
                var selection = SteamAppSelectionService.Select(candidate.App, options);
                if (candidate.App.AppId != appId || !ulong.TryParse(selection.BuildId, out var build) || build == 0
                    || string.IsNullOrWhiteSpace(selection.DisplayName) || string.IsNullOrWhiteSpace(selection.InstallDirectory)
                    || selection.Executables.Count == 0 || selection.Depots.Count == 0
                    || selection.Depots.Any(d => string.IsNullOrEmpty(d.Manifest)))
                    throw new SteamAppInfoException("缺少完整的版本、Depot 或启动信息");
                if (acf != null) SteamManifestUpdateGuard.Validate(acf, appId, selection.BuildId);
                valid.Add((candidate.App, candidate.Source, build));
            }
            catch (SteamAppInfoException ex) { diagnostics.Add(candidate.Source + "：" + ex.Message); }
        }
        // Target conflicts have already been excluded. Prefer the higher build to avoid replacing
        // known newer metadata with an older snapshot. Equal builds prefer caller input order.
        var selected = valid.OrderByDescending(c => c.Build).FirstOrDefault();
        if (selected.App == null)
            throw new SteamAppInfoException("没有找到与 Steam 状态一致的完整版本信息，原配置未修改。\n" + string.Join("\n", diagnostics));
        return new(selected.App, selected.Source, diagnostics);
    }
}
