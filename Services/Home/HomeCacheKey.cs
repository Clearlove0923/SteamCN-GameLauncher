using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

internal static class HomeCacheKey
{
    // 来源路由、区域策略或动画候选规则改变时递增，旧版本元数据就不会误命中新规则。
    private const string CacheKeyVersion = "4-video-variants";

    public static string Create(HomeContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 先按键排序再序列化，确保相同配置不受 Dictionary 枚举顺序影响。
        var options = request.ProviderOptions
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var canonical = JsonSerializer.Serialize(new
        {
            cacheKeyVersion = CacheKeyVersion,
            request.SchemaVersion,
            gameId = request.GameId.Trim(),
            executablePath = request.ExecutablePath?.Trim().Replace('/', '\\').ToLowerInvariant(),
            installDirectory = request.InstallDirectory?.Trim().Replace('/', '\\').ToLowerInvariant(),
            cacheFolderName = request.CacheFolderName?.Trim(),
            providerId = request.ProviderId.Trim(),
            locale = request.Locale.Trim(),
            providerOptions = options,
        }, HomeContentJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string CreateBackgroundHistoryKey(HomeContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.ProviderOptions
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // 历史背景按游戏、Provider、语言和来源参数隔离，但不绑定安装路径，
        // 避免游戏迁移目录后丢失已验证的官方下载来源。
        var canonical = JsonSerializer.Serialize(new
        {
            historyKeyVersion = "1",
            request.SchemaVersion,
            gameId = request.GameId.Trim(),
            providerId = request.ProviderId.Trim(),
            locale = request.Locale.Trim(),
            providerOptions = options,
        }, HomeContentJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string SafeSegment(string? value, string fallback)
    {
        var candidate = new string((value ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .Take(80).ToArray());
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }

    public static string FolderSegment(HomeContentRequest request)
    {
        // 用户要求优先以游戏安装目录名称分组；过滤无效字符和 Windows 设备名，
        // 防止配置中的路径片段逃出缓存根目录。
        var name = request.CacheFolderName?.Trim().TrimEnd(' ', '.');
        if (string.IsNullOrEmpty(name)) name = request.GameId;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(ch => !invalid.Contains(ch) && !char.IsControl(ch)).Take(80).ToArray())
            .Trim().TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safe) || new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }
            .Contains(safe, StringComparer.OrdinalIgnoreCase))
            return SafeSegment(request.GameId, "game");
        return safe;
    }
}
