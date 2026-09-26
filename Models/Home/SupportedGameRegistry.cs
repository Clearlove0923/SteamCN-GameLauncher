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
    public static readonly IReadOnlySet<string> AppIds = new HashSet<string>(StringComparer.Ordinal)
    {
        // 鸣潮 (Wuthering Waves) — KURO GAMES
        "3513350",
        // 绝区零 (Zenless Zone Zero) — miHoYo
        "4162040",
        // 崩坏3 (Honkai Impact 3rd) — miHoYo
        "1671200",
        // 燕云十六声 (Where Winds Meet) — NetEase Everstone
        "3564740",
        // 无限暖暖 (Infinity Nikki) — Infold Games
        "3164330",
        // 异环 (NTE) — Hotta Studio (简体 / 繁体)
        "4508340",
        "4706890",
        // 明日方舟:终末地 (Arknights: Endfield) — Hypergryph/Gryphline
        "4732690",
    };

    public static bool IsSupported(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) && AppIds.Contains(appId.Trim());
}