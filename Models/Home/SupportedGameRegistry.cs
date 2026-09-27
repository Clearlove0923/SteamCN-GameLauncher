using System.Text.Json;

namespace SteamCNGameLauncher.Models.Home;

/// <summary>
/// 从仓库唯一的游戏来源 JSON 建立 C# 侧索引；Python Worker 读取同一份文件。
/// 首页先匹配路径中的游戏安装目录，再用真实可执行文件名兜底，不依赖 Steam AppID 推断来源。
/// </summary>
public static class SupportedGameRegistry
{
    private const string ResourceName = "SteamCNGameLauncher.Config.home-content-sources.json";

    private sealed class SupportedSource
    {
        public string AppId { get; init; } = "";
        public string GameId { get; init; } = "";
        public string ProviderId { get; init; } = "";
        public string[] ExeNames { get; init; } = [];
        public string[] FolderNames { get; init; } = [];
        public Dictionary<string, JsonElement> ProviderOptions { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> NewsCategoryLabels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string[] NewsCategoryOrder { get; init; } = [];
    }

    public sealed record Match(
        string GameId,
        string ProviderId,
        IReadOnlyDictionary<string, JsonElement> ProviderOptions,
        IReadOnlyDictionary<string, string> NewsCategoryLabels,
        IReadOnlyList<string> NewsCategoryOrder,
        IReadOnlyList<string> FolderNames);

    private static readonly IReadOnlyList<SupportedSource> Sources = LoadSources();
    private static readonly IReadOnlyDictionary<string, SupportedSource> SourcesByAppId = Sources
        .Where(source => !string.IsNullOrWhiteSpace(source.AppId))
        .ToDictionary(source => source.AppId.Trim(), StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SupportedSource>> SourcesByExe = BuildExeIndex();
    private static readonly IReadOnlyDictionary<string, SupportedSource> SourcesByFolder = BuildIndex(source => source.FolderNames);

    public static IReadOnlySet<string> AppIds { get; } =
        new HashSet<string>(SourcesByAppId.Keys, StringComparer.Ordinal);

    private static IReadOnlyList<SupportedSource> LoadSources()
    {
        // 构建时嵌入 python/home_content/game_sources.json，避免两端维护不同游戏清单。
        using var stream = typeof(SupportedGameRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少首页来源配置资源：{ResourceName}");
        var sources = JsonSerializer.Deserialize<List<SupportedSource>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("首页来源配置为空。");
        if (sources.Any(source => string.IsNullOrWhiteSpace(source.GameId)
            || string.IsNullOrWhiteSpace(source.ProviderId)
            || !source.ProviderOptions.TryGetValue("region", out var region)
            || region.ValueKind != JsonValueKind.String
            || !string.Equals(region.GetString(), "cn", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("每个首页来源都必须提供 gameId、providerId 和 region=cn。");
        if (sources.Select(source => source.GameId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new InvalidOperationException("首页来源 gameId 不能重复。");
        return sources;
    }

    private static IReadOnlyDictionary<string, SupportedSource> BuildIndex(Func<SupportedSource, IEnumerable<string>> names)
    {
        // 索引只创建一次；大小写不敏感的完整名称匹配开销为常数级。
        var index = new Dictionary<string, SupportedSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources)
            foreach (var name in names(source))
            {
                var key = name.Trim();
                if (string.IsNullOrWhiteSpace(key) || index.ContainsKey(key))
                    throw new InvalidOperationException($"首页来源可执行文件或目录名为空或重复：{key}");
                index.Add(key, source);
            }
        return index;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SupportedSource>> BuildExeIndex()
    {
        // 不同游戏可能都有 launcher.exe。保留所有所属游戏，但无目录时只允许唯一 EXE 命中。
        var index = new Dictionary<string, List<SupportedSource>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources)
            foreach (var name in source.ExeNames)
            {
                var key = name.Trim();
                if (string.IsNullOrWhiteSpace(key) || key.Contains('\\') || key.Contains('/'))
                    throw new InvalidOperationException($"首页来源 EXE 名称无效：{key}");
                if (!index.TryGetValue(key, out var owners))
                    index[key] = owners = [];
                if (owners.Contains(source))
                    throw new InvalidOperationException($"首页来源 EXE 名称重复：{key}");
                owners.Add(source);
            }
        return index.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<SupportedSource>)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>按 EXE 路径和安装路径中由近到远的完整目录段优先匹配，最后用 EXE 名称兜底。</summary>
    public static bool TryMatch(string? executablePath, string? installDirectory, out Match match)
    {
        foreach (var (path, isExecutablePath) in new[] { (executablePath, true), (installDirectory, false) })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            var directoryCount = isExecutablePath ? segments.Length - 1 : segments.Length;
            for (var i = directoryCount - 1; i >= 0; i--)
            {
                var segment = segments[i];
                if (SourcesByFolder.TryGetValue(segment.Trim(), out var byFolder))
                {
                    match = ToMatch(byFolder);
                    return true;
                }
            }
        }
        var exe = (executablePath ?? "").Replace('/', '\\').Split('\\').Last().Trim();
        if (!string.IsNullOrWhiteSpace(exe) && SourcesByExe.TryGetValue(exe, out var owners)
            && owners.Count == 1)
        {
            match = ToMatch(owners[0]);
            return true;
        }
        match = null!;
        return false;
    }

    public static string GetCacheFolderName(Match match, string? executablePath, string? installDirectory)
    {
        // 优先使用用户真实路径中出现的官方游戏目录名，以便缓存按安装文件夹归档。
        foreach (var path in new[] { executablePath, installDirectory })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            foreach (var segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Reverse())
                if (match.FolderNames.Contains(segment.Trim(), StringComparer.OrdinalIgnoreCase))
                    return segment.Trim();
        }

        if (!string.IsNullOrWhiteSpace(installDirectory))
            return installDirectory.TrimEnd('\\', '/').Split(['\\', '/']).Last();
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var parts = executablePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return parts[^2];
        }
        return match.GameId;
    }

    private static Match ToMatch(SupportedSource source) => new(
        source.GameId,
        source.ProviderId,
        source.ProviderOptions.ToDictionary(pair => pair.Key,
            pair => pair.Value.Clone(), StringComparer.Ordinal),
        source.NewsCategoryLabels,
        source.NewsCategoryOrder,
        source.FolderNames);

    // 旧版预设编辑器仍依赖 AppID 查找入口；仅用于兼容，不参与新首页的游戏识别。
    public static bool IsSupported(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) && AppIds.Contains(appId.Trim());

    public static bool TryGetProviderId(string? appId, out string providerId)
    {
        if (!string.IsNullOrWhiteSpace(appId) && SourcesByAppId.TryGetValue(appId.Trim(), out var source))
        {
            providerId = source.ProviderId;
            return true;
        }
        providerId = "";
        return false;
    }

    public static string ResolveProviderId(string? appId, string? declaredProviderId, string fallbackProviderId) =>
        TryGetProviderId(appId, out var providerId)
            ? providerId
            : string.IsNullOrWhiteSpace(declaredProviderId) ? fallbackProviderId : declaredProviderId.Trim();

    public static IReadOnlyDictionary<string, JsonElement> GetProviderOptions(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) && SourcesByAppId.TryGetValue(appId.Trim(), out var source)
            ? ToMatch(source).ProviderOptions
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> GetNewsCategoryLabels(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) && SourcesByAppId.TryGetValue(appId.Trim(), out var source)
            ? source.NewsCategoryLabels
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
