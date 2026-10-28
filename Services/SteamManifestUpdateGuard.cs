using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamCNGameLauncher.Services;

/// <summary>Detects conflicting version evidence; passing this check does not prove freshness.</summary>
public static class SteamManifestUpdateGuard
{
    public static async Task EnsureWritableAsync(string path, string appId, string candidateBuildId,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(path))
            Validate(await File.ReadAllTextAsync(path, cancellationToken), appId, candidateBuildId);
        cancellationToken.ThrowIfCancellationRequested();
        var processes = Process.GetProcessesByName("steam");
        try
        {
            if (processes.Length > 0)
                throw new SteamAppInfoException("请先通过 Steam 菜单完全退出 Steam，再执行一键更新，避免配置被 Steam 写回覆盖。原配置未修改。");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static void Validate(string acf, string appId, string candidateBuildId)
    {
        var fields = ReadAppState(acf);
        if (!fields.TryGetValue("appid", out var existingAppId) || existingAppId != appId)
            throw new SteamAppInfoException("现有 ACF 的 AppID 不匹配，已停止更新。原配置未修改。");
        if (!ulong.TryParse(candidateBuildId, NumberStyles.None, CultureInfo.InvariantCulture, out var candidate) || candidate == 0)
            throw new SteamAppInfoException("来源没有有效的 BuildID，已停止更新。");
        var installed = ReadBuild(fields, "buildid");
        var target = ReadBuild(fields, "TargetBuildID");
        // Build IDs are identifiers, not semantic versions. Any target disagreement requires
        // reconciliation, even if the candidate looks numerically newer (branch/rollback cases).
        if (target != 0 && target != candidate)
            throw new SteamAppInfoException($"版本信息冲突：Steam 记录的目标 BuildID 为 {target}，游戏信息接口返回 {candidate}。接口可能尚未同步，或分支信息不同。请稍后重试；原配置未修改。不能沿用旧 Manifest 只修改 BuildID。");
        if (installed > candidate)
            throw new SteamAppInfoException($"无法确认最新版本：现有 BuildID 为 {installed}，接口返回 {candidate}。已阻止自动回退，原配置未修改。");
    }

    private static ulong ReadBuild(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value)) return 0;
        if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return id;
        throw new SteamAppInfoException($"现有 ACF 的 {key} 无效，无法安全比较版本。原配置未修改。");
    }

    // Parse the complete text structure, taking only direct AppState scalars. Nested depot
    // fields, comments, escaped quotes and braces in strings must not become version evidence.
    private static Dictionary<string, string> ReadAppState(string text)
    {
        var tokens = new List<string>();
        var pattern = new Regex("\\G(?:\\s+|//[^\\r\\n]*|\"(?:\\\\.|[^\"\\\\])*\"|[{}])", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        for (var position = 0; position < text.Length;)
        {
            var match = pattern.Match(text, position);
            if (!match.Success) throw Invalid();
            position += match.Length;
            if (!string.IsNullOrWhiteSpace(match.Value) && !match.Value.StartsWith("//")) tokens.Add(match.Value);
        }
        var index = 0;
        string Next() => index < tokens.Count ? tokens[index++] : throw Invalid();
        string Scalar(string token) => token.StartsWith('"') && token.EndsWith('"') ? token[1..^1] : throw Invalid();
        Dictionary<string, string> Object(int depth)
        {
            if (depth > 64 || Next() != "{") throw Invalid();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (index < tokens.Count && tokens[index] != "}")
            {
                var key = Scalar(Next());
                if (!keys.Add(key) || index >= tokens.Count) throw Invalid();
                if (tokens[index] == "{") Object(depth + 1);
                else result.Add(key, Scalar(Next()));
            }
            if (Next() != "}") throw Invalid();
            return result;
        }
        if (!string.Equals(Scalar(Next()), "AppState", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var fields = Object(0);
        if (index != tokens.Count) throw Invalid();
        return fields;
    }

    private static SteamAppInfoException Invalid() => new("现有 ACF 格式无法识别，无法安全比较版本。原配置未修改。");
}
