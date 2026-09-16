namespace SteamCNGameLauncher.Models;

public static class AppearancePageIds
{
    public const string Home = "home";
    public const string GameConfiguration = "game-configuration";
    public const string Screenshots = "screenshots";
    public const string Appearance = "appearance";
    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } =
        [Home, GameConfiguration, Screenshots, Appearance, Settings];

    public static string Normalize(string? value) => All.Contains(value) ? value! : Home;
}

/// <summary>全局外观状态；图片效果和裁切参数按图库文件名分别保存。</summary>
public sealed class AppearanceSettings : AppearanceProfile
{
    public bool UsePerPageSettings { get; set; }
    public bool PerPageSettingsInitialized { get; set; }
    public Dictionary<string, AppearanceProfile> Pages { get; set; } = new();

    public AppearanceProfile GetEffective(string? pageId)
    {
        Pages ??= new();
        if (!UsePerPageSettings)
            return this;

        var normalized = AppearancePageIds.Normalize(pageId);
        if (!Pages.TryGetValue(normalized, out var profile) || profile is null)
            Pages[normalized] = profile = CloneProfile();
        profile.Normalize();
        return profile;
    }

    public void InitializePerPageSettings()
    {
        Pages ??= new();
        if (PerPageSettingsInitialized)
            return;
        foreach (var pageId in AppearancePageIds.All)
            Pages[pageId] = CloneProfile();
        PerPageSettingsInitialized = true;
    }

    public IEnumerable<AppearanceProfile> EnumerateProfiles()
    {
        yield return this;
        if (Pages is null) yield break;
        foreach (var profile in Pages.Values.OfType<AppearanceProfile>())
            yield return profile;
    }
}

public class AppearanceProfile
{
    public bool Enabled { get; set; }
    // Null preserves the theme's original card opacity for existing settings.
    public double? CardOpacity { get; set; }
    public bool ShowHomeAnimation { get; set; } = true;
    public bool ShowHomeNews { get; set; } = true;
    public string SelectedImage { get; set; } = "";
    public Dictionary<string, BackgroundOptions> Images { get; set; } = new();

    // 便捷访问器不单独序列化，避免与 Images 中的参数重复。
    [System.Text.Json.Serialization.JsonIgnore]
    public BackgroundOptions Current
    {
        get
        {
            Images ??= new();
            if (!Images.TryGetValue(SelectedImage, out var options) || options == null)
                Images[SelectedImage] = options = new();
            return options;
        }
    }

    public AppearanceProfile CloneProfile()
    {
        Normalize();
        return new AppearanceProfile
        {
            Enabled = Enabled,
            CardOpacity = CardOpacity,
            ShowHomeAnimation = ShowHomeAnimation,
            ShowHomeNews = ShowHomeNews,
            SelectedImage = SelectedImage,
            Images = Images.ToDictionary(pair => pair.Key, pair => pair.Value?.Clone() ?? new(),
                StringComparer.OrdinalIgnoreCase),
        };
    }

    public void Normalize()
    {
        SelectedImage ??= "";
        Images ??= new();
    }
}

public sealed class BackgroundOptions
{
    // 旧配置没有尺寸字段，默认值用于将原 1440×810 坐标迁移到当前画布。
    public string SourceImage { get; set; } = "";
    public int CropWidth { get; set; } = 1440;
    public int CropHeight { get; set; } = 810;
    public double CropScale { get; set; }
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double Opacity { get; set; } = 0.6;
    public double OverlayOpacity { get; set; } = 0.25;
    public string Stretch { get; set; } = "UniformToFill";

    public BackgroundOptions Clone() => new()
    {
        SourceImage = SourceImage,
        CropWidth = CropWidth,
        CropHeight = CropHeight,
        CropScale = CropScale,
        CropX = CropX,
        CropY = CropY,
        Opacity = Opacity,
        OverlayOpacity = OverlayOpacity,
        Stretch = Stretch,
    };
}
