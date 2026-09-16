using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

Check(ReleaseVersionComparer.IsNewer("v2.7.0", "v2.6.1 (Release)"),
    "newer stable release is detected");
Check(!ReleaseVersionComparer.IsNewer("v2.6.1", "v2.6.1 (Release)"),
    "same stable release is ignored");
Check(!ReleaseVersionComparer.IsNewer("v2.5.9", "v2.6.1 (Release)"),
    "older stable release is ignored");
Check(ReleaseVersionComparer.IsNewer("v2.7.0-beta.1", "v2.6.1 (Release)"),
    "prerelease with newer core version is detected");
Check(ReleaseVersionComparer.IsNewer("v2.7.0-beta.2", "v2.7.0 (Beta 1)"),
    "newer prerelease sequence is detected");
Check(!ReleaseVersionComparer.IsNewer("v2.7.0-beta.2", "v2.7.0 (Release)"),
    "prerelease never replaces stable release with the same core version");
Check(ReleaseVersionComparer.IsNewer("v2.7.0", "v2.7.0 (Beta 4)"),
    "stable release replaces prerelease with the same core version");

var releases = new[]
{
    new GitHubReleaseInfo { TagName = "v9.0.0", Draft = true, PublishedAt = DateTimeOffset.Parse("2026-09-17") },
    new GitHubReleaseInfo { TagName = "v2.8.0-beta.1", Prerelease = true, PublishedAt = DateTimeOffset.Parse("2026-09-16") },
    new GitHubReleaseInfo { TagName = "v2.7.0", PublishedAt = DateTimeOffset.Parse("2026-09-15") },
};
Check(GitHubReleaseSelector.SelectLatest(releases, includePrerelease: true)?.TagName == "v2.8.0-beta.1",
    "beta channel selects newest non-draft release");
Check(GitHubReleaseSelector.SelectLatest(releases, includePrerelease: false)?.TagName == "v2.7.0",
    "stable channel excludes draft and prerelease releases");
var apiRelease = JsonSerializer.Deserialize<GitHubReleaseInfo>(
    """{"tag_name":"v2.7.1","html_url":"https://github.com/example/repo/releases/tag/v2.7.1","prerelease":false,"published_at":"2026-09-17T00:00:00Z"}""");
Check(apiRelease?.TagName == "v2.7.1" && apiRelease.PublishedAt is not null,
    "GitHub Releases API field names deserialize correctly");

Console.WriteLine($"All {checks} checks passed.");
