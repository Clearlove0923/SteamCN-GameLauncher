using System.Text.Json;
using SteamKit2;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

/// <summary>One isolated anonymous PICS session per request; no disk cache or account credentials.</summary>
public sealed class SteamLiveAppInfoService : ISteamAppInfoService
{
    private readonly TimeSpan _timeout;
    public SteamLiveAppInfoService(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(25);

    public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalized))
            throw new SteamAppInfoException("AppID 必须为有效的正整数。");
        return Task.Run(() => Query(normalized, cancellationToken), cancellationToken);
    }

    private SteamAppInfo Query(string appId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        var client = new SteamClient(SteamConfiguration.Create(b => b.WithProtocolTypes(ProtocolTypes.WebSocket | ProtocolTypes.Tcp)
            .WithMachineInfoProvider(AnonymousQueryIdentity.Instance)));
        var manager = new CallbackManager(client);
        var user = client.GetHandler<SteamUser>()!;
        var apps = client.GetHandler<SteamApps>()!;
        var id = uint.Parse(appId, System.Globalization.CultureInfo.InvariantCulture);
        SteamAppInfo? result = null;
        string? error = null;
        var reconnects = 0;
        using var connected = manager.Subscribe<SteamClient.ConnectedCallback>(_ => user.LogOnAnonymous());
        using var disconnected = manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            if (!deadline.IsCancellationRequested && reconnects++ < 1) client.Connect();
            else error = "Steam 连接已断开。";
        });
        using var loggedOn = manager.Subscribe<SteamUser.LoggedOnCallback>(callback =>
        {
            if (callback.Result != EResult.OK) { error = $"Steam 匿名连接失败：{callback.Result}"; return; }
            apps.PICSGetAccessTokens(id, null);
        });
        using var tokens = manager.Subscribe<SteamApps.PICSTokensCallback>(callback =>
        {
            callback.AppTokens.TryGetValue(id, out var accessToken);
            apps.PICSGetProductInfo(new SteamApps.PICSRequest(id, accessToken), null);
        });
        using var products = manager.Subscribe<SteamApps.PICSProductInfoCallback>(callback =>
        {
            if (callback.Apps.TryGetValue(id, out var info))
                result = ConvertProduct(info.KeyValues, appId) with
                {
                    Source = "Steam 直连查询（公开产品信息）", RetrievedAt = DateTimeOffset.UtcNow
                };
            else if (!callback.ResponsePending) error = "Steam 未提供该游戏的公开产品信息。";
        });
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            client.Connect();
            while (result == null && error == null)
            {
                deadline.Token.ThrowIfCancellationRequested();
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }
            deadline.Token.ThrowIfCancellationRequested();
            return result ?? throw new SteamAppInfoException(error!);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new SteamAppInfoException("Steam 直连查询超时。"); }
        catch (OperationCanceledException) { throw; }
        catch (SteamAppInfoException) { throw; }
        catch (Exception ex) { throw new SteamAppInfoException("Steam 直连查询失败：" + ex.Message, ex); }
        finally { client.Disconnect(); }
    }

    public static SteamAppInfo ConvertProduct(KeyValue root, string appId)
    {
        object Convert(KeyValue node, int depth)
        {
            if (depth > 64) throw new SteamAppInfoException("Steam 产品信息嵌套过深。");
            if (node.Children.Count == 0) return node.Value ?? "";
            var fields = new Dictionary<string, object>();
            foreach (var child in node.Children)
                if (child.Name == null || !fields.TryAdd(child.Name, Convert(child, depth + 1)))
                    throw new SteamAppInfoException("Steam 产品信息含重复或无效字段。");
            return fields;
        }
        if (root["appid"].Value != appId) throw new SteamAppInfoException("Steam 返回的 AppID 不匹配或数据不完整。");
        return SteamAppInfoParser.Parse(JsonSerializer.Serialize(new
        {
            status = "success", data = new Dictionary<string, object> { [appId] = Convert(root, 0) }
        }), appId);
    }

    // Public metadata queries do not need the user's hardware identifiers. Keep an ephemeral
    // identity for this process instead of reading machine GUID, adapter registry or disk IDs.
    private sealed class AnonymousQueryIdentity : IMachineInfoProvider
    {
        public static AnonymousQueryIdentity Instance { get; } = new();
        private readonly byte[] _id = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        public byte[] GetMachineGuid() => (byte[])_id.Clone();
        public byte[] GetMacAddress() => (byte[])_id.Clone();
        public byte[] GetDiskId() => (byte[])_id.Clone();
    }
}

/// <summary>The fallback is explicitly labeled; cancellation never starts another query.</summary>
public sealed class FreshSteamAppInfoService(ISteamAppInfoService? live = null, ISteamAppInfoService? fallback = null)
    : ISteamAppInfoService
{
    private readonly ISteamAppInfoService _live = live ?? new SteamLiveAppInfoService();
    private readonly ISteamAppInfoService _fallback = fallback ?? new SteamAppInfoService();
    public async Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        try { return await _live.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false); }
        catch (SteamAppInfoException liveError)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var app = await _fallback.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
                return app with { Source = "第三方备用接口（非实时保证；" + liveError.Message + "）", RetrievedAt = DateTimeOffset.UtcNow };
            }
            catch (SteamAppInfoException fallbackError)
            { throw new SteamAppInfoException(liveError.Message + " " + fallbackError.Message, fallbackError); }
        }
    }
}
