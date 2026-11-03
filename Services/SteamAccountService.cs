using System.Text;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

public sealed record SteamAccountDiscoveryResult(
    IReadOnlyList<SteamAccountInfo> Accounts,
    string LoginUsersPath,
    string Error)
{
    public bool IsSuccess => Accounts.Count > 0 && string.IsNullOrEmpty(Error);
}

/// <summary>Discovers local Steam accounts without contacting Steam or a third-party website.</summary>
public sealed class SteamAccountService
{
    public SteamAccountDiscoveryResult Discover(string? steamInstallPath)
    {
        var root = NormalizeSteamRoot(steamInstallPath);
        if (string.IsNullOrWhiteSpace(root))
            return Failure("请先填写或自动检测 Steam 安装路径。");

        var path = Path.Combine(root, "config", "loginusers.vdf");
        if (!File.Exists(path))
            return Failure("未找到 config\\loginusers.vdf；请确认 Steam 安装路径，或先登录一次 Steam。", path);

        try
        {
            var accounts = Parse(File.ReadAllText(path))
                .OrderByDescending(account => account.IsMostRecent)
                .ThenByDescending(account => account.Timestamp)
                .ThenBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return accounts.Count == 0
                ? Failure("loginusers.vdf 中没有找到有效的 SteamID64。", path)
                : new(accounts, path, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return Failure($"无法读取 Steam 账号配置：{ex.Message}", path);
        }
    }

    public static IReadOnlyList<SteamAccountInfo> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var tokens = Tokenize(content);
        var usersIndex = FindKey(tokens, "users");
        if (usersIndex < 0 || usersIndex + 1 >= tokens.Count || tokens[usersIndex + 1] != "{")
            return Array.Empty<SteamAccountInfo>();

        var accounts = new List<SteamAccountInfo>();
        var index = usersIndex + 2;
        while (index < tokens.Count && tokens[index] != "}")
        {
            var steamId = tokens[index++];
            if (index >= tokens.Count || tokens[index] != "{")
            {
                index++;
                continue;
            }

            index++;
            var values = ReadFlatObject(tokens, ref index);
            if (!IsSteamId64(steamId)) continue;

            values.TryGetValue("AccountName", out var accountName);
            values.TryGetValue("PersonaName", out var personaName);
            values.TryGetValue("MostRecent", out var mostRecent);
            values.TryGetValue("Timestamp", out var timestampText);
            _ = long.TryParse(timestampText, out var timestamp);
            accounts.Add(new(
                steamId,
                accountName ?? "",
                personaName ?? "",
                string.Equals(mostRecent, "1", StringComparison.Ordinal),
                timestamp));
        }

        return accounts
            .GroupBy(account => account.SteamId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static Dictionary<string, string> ReadFlatObject(IReadOnlyList<string> tokens, ref int index)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (index < tokens.Count && tokens[index] != "}")
        {
            var key = tokens[index++];
            if (index >= tokens.Count) break;
            if (tokens[index] == "{")
            {
                SkipObject(tokens, ref index);
                continue;
            }
            result[key] = tokens[index++];
        }
        if (index < tokens.Count && tokens[index] == "}") index++;
        return result;
    }

    private static void SkipObject(IReadOnlyList<string> tokens, ref int index)
    {
        var depth = 0;
        do
        {
            if (tokens[index] == "{") depth++;
            else if (tokens[index] == "}") depth--;
            index++;
        } while (index < tokens.Count && depth > 0);
    }

    private static int FindKey(IReadOnlyList<string> tokens, string key)
    {
        for (var i = 0; i < tokens.Count; i++)
            if (string.Equals(tokens[i], key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static List<string> Tokenize(string content)
    {
        var tokens = new List<string>();
        for (var index = 0; index < content.Length;)
        {
            if (char.IsWhiteSpace(content[index])) { index++; continue; }
            if (content[index] is '{' or '}') { tokens.Add(content[index++].ToString()); continue; }
            if (content[index] != '"') { while (index < content.Length && !char.IsWhiteSpace(content[index])) index++; continue; }

            index++;
            var value = new StringBuilder();
            while (index < content.Length && content[index] != '"')
            {
                if (content[index] == '\\' && index + 1 < content.Length && content[index + 1] is '\\' or '"')
                    index++;
                value.Append(content[index++]);
            }
            if (index >= content.Length) throw new FormatException("loginusers.vdf 包含未结束的字符串。");
            index++;
            tokens.Add(value.ToString());
        }
        return tokens;
    }

    private static bool IsSteamId64(string value)
        => value.Length == 17 && value.StartsWith("7656119", StringComparison.Ordinal)
            && ulong.TryParse(value, out _);

    private static string NormalizeSteamRoot(string? path)
    {
        var value = path?.Trim().Trim('"') ?? "";
        return string.Equals(Path.GetFileName(value), "steam.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(value) ?? ""
            : value;
    }

    private static SteamAccountDiscoveryResult Failure(string error, string path = "")
        => new(Array.Empty<SteamAccountInfo>(), path, error);
}
