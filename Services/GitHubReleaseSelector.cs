using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

public static class GitHubReleaseSelector
{
    public static GitHubReleaseInfo? SelectLatest(
        IEnumerable<GitHubReleaseInfo> releases,
        bool includePrerelease) => releases
        .Where(release => !release.Draft && (includePrerelease || !release.Prerelease))
        .OrderByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
        .FirstOrDefault();
}
