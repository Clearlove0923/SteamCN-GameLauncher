namespace SteamCNGameLauncher.Models.Home;

/// <summary>
/// 已通过端到端验证、Provider 能拉到真实首页内容的 Steam AppId 白名单。
/// 启动器在加载首页背景前先查这个清单；不在清单里的 AppId
/// 一律走纯黑默认背景（忽略外观设置中的 SourceImage），
/// 避免在未验证游戏上误显示测试图或主题残留底色。
///
/// 维护规则：每接入一个新游戏的真实首页内容并端到端验证通过后，
/// 在这里追加一行 AppId；不要把"理论上能跑"的 ID 列进来。
/// </summary>
public static class SupportedGameRegistry
{
    private static readonly IReadOnlyDictionary<string, string> ProvidersByAppId =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // 鸣潮 (Wuthering Waves) — KURO GAMES
        ["3513350"] = "kuro-launcher",
        // 绝区零 (Zenless Zone Zero) — miHoYo
        ["4162040"] = "hoyoplay-json",
        // 崩坏3 (Honkai Impact 3rd) — miHoYo
        ["1671200"] = "hoyoplay-json",
        // 燕云十六声 (Where Winds Meet) — NetEase Everstone
        ["3564740"] = "netease-static-cms",
        // 无限暖暖 (Infinity Nikki) — Infold Games
        ["3164330"] = "nextjs-data",
        // 异环 (NTE) — Hotta Studio (简体 / 繁体)
        ["4508340"] = "perfect-world-hybrid",
        ["4706890"] = "perfect-world-hybrid",
        // 明日方舟:终末地 (Arknights: Endfield) — Hypergryph/Gryphline
        ["4732690"] = "hypergryph-batch",
    };

    public static IReadOnlySet<string> AppIds { get; } =
        new HashSet<string>(ProvidersByAppId.Keys, StringComparer.Ordinal);

    public static bool IsSupported(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) && AppIds.Contains(appId.Trim());

    /// <summary>
    /// 返回已验证 AppId 对应的稳定 Provider ID。游戏预设显式保存的 Provider
    /// 仍然具有更高优先级；此映射只负责兼容旧配置和减少首次配置步骤。
    /// </summary>
    public static bool TryGetProviderId(string? appId, out string providerId)
    {
        if (!string.IsNullOrWhiteSpace(appId) &&
            ProvidersByAppId.TryGetValue(appId.Trim(), out var resolved))
        {
            providerId = resolved;
            return true;
        }

        providerId = string.Empty;
        return false;
    }
}
