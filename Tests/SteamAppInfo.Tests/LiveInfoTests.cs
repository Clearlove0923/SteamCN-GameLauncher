using SteamKit2;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

internal static class LiveInfoTests
{
    public static async Task Run(Action<bool, string> check, SteamAppInfo example)
    {
        var tree = KeyValue.LoadFromString("""
        "appinfo" {
          "appid" "4706890"
          "common" { "name" "異環" }
          "config" { "installdir" "異環" "launch" { "0" { "executable" "NTETW/NTETWLauncher.exe" "arguments" "/launcher" } } }
          "depots" { "4706891" { "manifests" { "public" { "gid" "5084594620537324499" } } } "branches" { "public" { "buildid" "26236512" } } }
        }
        """)!;
        var parsed = SteamLiveAppInfoService.ConvertProduct(tree, "4706890");
        check(parsed.BranchBuildIds["public"] == "26236512" && parsed.Depots.Single().Manifests["public"] == "5084594620537324499"
            && parsed.LaunchOptions.Single().Arguments == "/launcher", "PICS snapshot preserves build, depot, 64-bit manifest and launch arguments");
        try { SteamLiveAppInfoService.ConvertProduct(tree, "123"); throw new Exception("mismatched app accepted"); }
        catch (SteamAppInfoException) { check(true, "PICS rejects wrong AppID"); }
        var fallback = new Stub(() => Task.FromResult(example));
        var live = new Stub(() => Task.FromResult(example with { Source = "live" }));
        var first = await new FreshSteamAppInfoService(live, fallback).GetAppInfoAsync("123");
        check(first.Source == "live" && fallback.Calls == 0, "successful live request never invokes third-party API");
        var failure = new Stub(() => throw new SteamAppInfoException("timeout fixture"));
        var second = await new FreshSteamAppInfoService(failure, fallback).GetAppInfoAsync("123");
        check(second.Source.Contains("第三方备用") && second.Source.Contains("timeout fixture") && second.RetrievedAt != null,
            "live failure labels fallback and query time honestly");
        var cancelled = new Stub(() => throw new OperationCanceledException());
        var previous = fallback.Calls;
        try { await new FreshSteamAppInfoService(cancelled, fallback).GetAppInfoAsync("123"); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { check(fallback.Calls == previous, "cancellation does not start fallback query"); }
        try { await new SteamLiveAppInfoService().GetAppInfoAsync("0"); throw new Exception("invalid app accepted"); }
        catch (SteamAppInfoException) { check(true, "invalid live AppID rejected before network"); }
    }
    private sealed class Stub(Func<Task<SteamAppInfo>> action) : ISteamAppInfoService
    {
        public int Calls { get; private set; }
        public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
        { Calls++; return action(); }
    }
}
