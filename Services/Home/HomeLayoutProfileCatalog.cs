using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>通过持久化布局 Id 解析首页尺寸，后续 Provider 可为游戏指定其他档案。</summary>
public sealed class HomeLayoutProfileCatalog
{
    public const string DefaultProfileId = HomeLayoutProfile.MihoyoLauncherId;

    private readonly IReadOnlyDictionary<string, HomeLayoutProfile> _profiles =
        new Dictionary<string, HomeLayoutProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultProfileId] = new(
                DefaultProfileId,
                NewsWidth: 576,
                NewsHeight: 425,
                NewsHeroHeight: 261.5,
                NewsLeft: 90,
                NewsBottom: 76,
                LaunchRight: 90,
                LaunchBottom: 90,
                StartButtonWidth: 186,
                StartButtonHeight: 84,
                LaunchMenuButtonWidth: 93)
        };

    public HomeLayoutProfile Resolve(string? id) =>
        !string.IsNullOrWhiteSpace(id) && _profiles.TryGetValue(id, out var profile)
            ? profile
            : _profiles[DefaultProfileId];
}
