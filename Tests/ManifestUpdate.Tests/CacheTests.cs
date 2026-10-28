using System.Text;
using System.Text.Json;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

internal static class CacheTests
{
    // Sanitized local NTE metadata, verified 2026-10-28; no account/token/hash data.
    private const string Metadata = """
    {"appid":4706890,"common":{"name":"異環"},
     "config":{"installdir":"異環","launch":{"0":{"executable":"NTETW/NTETWLauncher.exe","type":"default","config":{"oslist":"windows","osarch":"64"}}}},
     "depots":{"4706891":{"manifests":{"public":{"gid":"5084594620537324499"}}},"branches":{"public":{"buildid":"26236512"}}}}
    """;

    public static async Task Run(Action<bool, string> check, string pending)
    {
        SteamAppInfo? local = null;
        foreach (var version in new uint[] { 0x07564427, 0x07564428, 0x07564429 })
        {
            using var fixture = Fixture(version);
            local = SteamAppCacheReader.Read(fixture, "4706890");
            check(local.BranchBuildIds["public"] == "26236512"
                && local.Depots.Single().Manifests["public"] == "5084594620537324499",
                $"cache format {version:X} preserves matching build and manifest");
        }
        var remote = local! with
        {
            BranchBuildIds = new Dictionary<string, string> { ["public"] = "26092471" },
            Depots = new[] { local!.Depots[0] with { Manifests = new Dictionary<string, string> { ["public"] = "3646058931896810596" } } }
        };
        var options = new SteamAppSelectionOptions();
        var chosen = SteamUpdateInfoResolver.Choose(new[] { (local!, "local"), (remote, "remote") }, pending, "4706890", options);
        check(chosen.Source == "local" && chosen.App.Depots[0].Manifests["public"] == "5084594620537324499"
            && chosen.Diagnostics.Count == 1, "NTE chooses complete local snapshot and rejects stale API");
        var reversed = SteamUpdateInfoResolver.Choose(new[] { (remote, "local"), (local!, "remote") }, pending, "4706890", options);
        check(reversed.Source == "remote", "stale local cache falls back to matching remote snapshot");
        var incomplete = local! with { Depots = Array.Empty<SteamDepotInfo>() };
        try
        {
            SteamUpdateInfoResolver.Choose(new[] { (incomplete, "local"), (remote, "remote") }, pending, "4706890", options);
            throw new Exception("incomplete local metadata was accepted");
        }
        catch (SteamAppInfoException) { check(true, "missing new manifest cannot be substituted with stale remote manifest"); }
        var noEvidence = SteamUpdateInfoResolver.Choose(new[] { (remote, "local"), (local!, "remote") }, null, "4706890", options);
        check(noEvidence.Source == "remote", "newer complete metadata wins when ACF is missing");
        foreach (var corrupt in new[] { new byte[16], Fixture(0x07564429).ToArray()[..30] })
        {
            try { SteamAppCacheReader.Read(new MemoryStream(corrupt), "4706890"); throw new Exception("corrupt cache accepted"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { check(true, "corrupt or unsupported cache is rejected"); }
        }
        using (var absent = Fixture(0x07564429))
        {
            try { SteamAppCacheReader.Read(absent, "123"); throw new Exception("missing app accepted"); }
            catch (InvalidDataException) { check(true, "missing app is rejected"); }
        }
        using (var canceled = Fixture(0x07564429))
        {
            try { SteamAppCacheReader.Read(canceled, "4706890", new CancellationToken(true)); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { check(true, "cache parsing honors cancellation"); }
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "appinfo.vdf");
            await File.WriteAllBytesAsync(path, Fixture(0x07564429).ToArray());
            var acfPath = Path.Combine(directory, "app.acf");
            await File.WriteAllTextAsync(acfPath, pending);
            var offline = await new SteamUpdateInfoResolver(new Offline()).ResolveAsync("4706890", path, acfPath, options);
            check(offline.App.BranchBuildIds["public"] == "26236512", "network failure still uses complete local cache");
            await File.WriteAllBytesAsync(path, new byte[16]);
            var fallback = await new SteamUpdateInfoResolver(new Fixed(local!)).ResolveAsync("4706890", path, acfPath, options);
            check(fallback.Source == "游戏信息接口", "unsupported cache falls back to complete remote metadata");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Offline : ISteamAppInfoService
    {
        public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
            => throw new SteamAppInfoException("offline fixture");
    }

    private sealed class Fixed(SteamAppInfo app) : ISteamAppInfoService
    {
        public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
            => Task.FromResult(app);
    }

    private static MemoryStream Fixture(uint version)
    {
        using var doc = JsonDocument.Parse(Metadata);
        var keys = new List<string>();
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, true);
        void CString(string text) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
        void Key(string key)
        {
            if (version != 0x07564429) { CString(key); return; }
            if (!keys.Contains(key)) keys.Add(key);
            writer.Write((uint)keys.IndexOf(key));
        }
        void Entry(string key, JsonElement value)
        {
            writer.Write((byte)(value.ValueKind == JsonValueKind.Object ? 0 : value.ValueKind == JsonValueKind.Number ? 2 : 1));
            Key(key);
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject()) Entry(property.Name, property.Value);
                writer.Write((byte)8);
            }
            else if (value.ValueKind == JsonValueKind.Number) writer.Write(value.GetInt32());
            else CString(value.GetString()!);
        }
        Entry("appinfo", doc.RootElement);
        writer.Write((byte)8);
        var result = new MemoryStream();
        using var output = new BinaryWriter(result, Encoding.UTF8, true);
        output.Write(version); output.Write(1u);
        if (version == 0x07564429) output.Write(0L);
        var header = version == 0x07564427 ? 40 : 60;
        output.Write(4706890u); output.Write((uint)(header + payload.Length));
        output.Write(new byte[header]); output.Write(payload.ToArray()); output.Write(0u);
        if (version == 0x07564429)
        {
            var offset = result.Position;
            output.Write((uint)keys.Count);
            foreach (var key in keys) { output.Write(Encoding.UTF8.GetBytes(key)); output.Write((byte)0); }
            result.Position = 8; output.Write(offset);
        }
        result.Position = 0;
        return result;
    }
}
