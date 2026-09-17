namespace SteamCNGameLauncher.Services;

/// <summary>比较 GitHub tag 与应用显示版本，支持 v1.2.3、v1.2.3-beta.2 和 v1.2.3 (Beta 2)。</summary>
public static class ReleaseVersionComparer
{
    public static bool IsNewer(string? remoteTag, string? localVersion)
    {
        if (!TryParse(remoteTag, out var remote) || !TryParse(localVersion, out var local))
            return false;

        var coreComparison = remote.Core.CompareTo(local.Core);
        if (coreComparison != 0)
            return coreComparison > 0;

        // 同一主版本下，正式版高于任何预发布版。
        if (remote.IsPrerelease != local.IsPrerelease)
            return !remote.IsPrerelease;
        return remote.IsPrerelease && remote.PrereleaseNumber > local.PrereleaseNumber;
    }

    private static bool TryParse(string? value, out ParsedReleaseVersion parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+', '(']);
        var coreText = (suffixIndex >= 0 ? normalized[..suffixIndex] : normalized).Trim();
        if (!Version.TryParse(coreText, out var core)) return false;

        var suffix = suffixIndex >= 0 ? normalized[suffixIndex..] : "";
        var isPrerelease = suffix.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            || suffix.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || suffix.Contains("preview", StringComparison.OrdinalIgnoreCase)
            || suffix.Contains("pre", StringComparison.OrdinalIgnoreCase)
            || suffix.Contains("rc", StringComparison.OrdinalIgnoreCase);
        var number = 0;
        if (isPrerelease)
        {
            var digits = new string(suffix.Reverse().SkipWhile(character => !char.IsDigit(character))
                .TakeWhile(char.IsDigit).Reverse().ToArray());
            _ = int.TryParse(digits, out number);
        }

        parsed = new ParsedReleaseVersion(core, isPrerelease, number);
        return true;
    }

    private readonly record struct ParsedReleaseVersion(Version Core, bool IsPrerelease, int PrereleaseNumber);
}
