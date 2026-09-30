using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;
using SteamCNGameLauncher.Services.Update;
using System.Text.Json;
using System.Text;
using System.Buffers.Binary;
using System.Text.Json.Nodes;

if (args.Length == 3 && args[0] == "--prepare-session")
{
    var session = await KachinaSessionPackage.CreateAsync(args[1],
        new LauncherUpdateInfo("v3.1.2", "更新功能\n统一更新窗口，支持 CNB / GitHub 来源切换。\n修复问题\n修复检查更新忽略用户首选源。",
            UpdateSourcePolicy.GetManualDownloadUrl("cnb"), "cnb"), args[2]);
    Console.WriteLine("SESSION=" + session);
    return;
}

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

Check(UpdateSourcePolicy.Normalize(null) == UpdateSourceIds.Cnb,
    "CNB is the default update download source");
Check(UpdateSourcePolicy.Normalize("unknown") == UpdateSourceIds.Cnb,
    "unknown persisted update source safely falls back to CNB");
Check(UpdateSourcePolicy.Normalize(UpdateSourceIds.GitHub) == UpdateSourceIds.GitHub,
    "GitHub update source remains selectable");
Check(UpdateSourcePolicy.GetManualDownloadUrl(UpdateSourceIds.Cnb)
        == "https://cnb.cool/SteamCN-GameLauncher/SteamCN-GameLauncher/-/releases/latest",
    "CNB manual fallback opens the configured CNB release page");
Check(UpdateSourcePolicy.GetManualDownloadUrl(UpdateSourceIds.GitHub)
        == "https://github.com/Clearlove0923/SteamCN-GameLauncher/releases/latest",
    "GitHub manual fallback opens the configured GitHub release page");

Check(UpdateSourcePolicy.GetPackageUrl("cnb", "v3.1.2") ==
    "https://cnb.cool/SteamCN-GameLauncher/SteamCN-GameLauncher/-/releases/download/v3.1.2/SteamCN-GameLauncher.Install.3.1.2.exe",
    "CNB package is anonymous HTTPS and pinned to the displayed tag");
Check(UpdateSourcePolicy.GetPackageUrl("github", "v3.2.0-beta.1").EndsWith(
    "/v3.2.0-beta.1/SteamCN-GameLauncher.Install.3.2.0-beta.1.exe"), "prerelease package keeps its complete tag");
foreach (var invalid in new[] { "../../evil", "https://evil.test", "v3.1.2/evil", "v3.1.2?token=x" })
{
    try { UpdateSourcePolicy.GetPackageUrl("cnb", invalid); throw new Exception("accepted unsafe tag"); }
    catch (InvalidDataException) { Check(true, "reject unsafe package tag: " + invalid); }
}

// CNB 公开页面 2026-09-30 数据结构，去除用户信息；额外草稿/预发布条目仅用于固定回归。
var cnbItems = new object[]
{
    new { tag_ref = "refs/tags/v9.0.0", title = "draft", body = "hidden", is_draft = true, is_prerelease = false, published_at = "2026-09-30T20:00:00Z" },
    new { tag_ref = "refs/tags/v3.2.0-beta.1", title = "preview", body = "beta", is_draft = false, is_prerelease = true, published_at = "2026-09-30T19:00:00Z" },
    new { tag_ref = "refs/tags/v3.1.2", title = "SteamCN-GameLauncher v3.1.2", body = "## 更新功能\n- 窗口\n## 修复 Bug\n- 来源", is_draft = false, is_prerelease = false, published_at = "2026-09-29T16:27:08Z" }
};
var pageData = JsonSerializer.Serialize(new { props = new { pageProps = new { initialState = new { slug = new { repo = new { releases = new { list = new { data = new { releases = cnbItems } } } } } } } } });
var html = "<html><a href='/releases/tag/v99.0.0'>untrusted link</a><script type='application/json' id='__NEXT_DATA__'>" + pageData + "</script></html>";
var stable = CnbReleasePageParser.Parse(html, false);
Check(stable.TagName == "v3.1.2", "CNB excludes drafts/prereleases and ignores unrelated HTML links");
Check(stable.Body == "## 更新功能\n- 窗口\n## 修复 Bug\n- 来源", "CNB preserves complete UTF-8 release notes");
Check(CnbReleasePageParser.Parse(html, true).TagName == "v3.2.0-beta.1", "CNB beta option selects public prerelease");
try { CnbReleasePageParser.Parse("<html>login required</html>", false); throw new Exception("accepted missing data"); }
catch (InvalidDataException) { Check(true, "missing public page data fails instead of claiming no updates"); }

var work = Path.Combine(Path.GetTempPath(), "SteamCN-UpdateTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
string? sessionPath = null;
try
{
    var config = Encoding.UTF8.GetBytes("""{"exeName":"SteamCN-GameLauncher.exe","source":[{"id":"cnb","uri":"old"},{"id":"github","uri":"old"}]}""");
    var theme = Encoding.UTF8.GetBytes("/* fixture theme */ .image { background: none; }");
    var template = Path.Combine(work, "template.exe");
    using (var stream = File.Create(template))
    {
        stream.Write("MZ-test-native-code"u8);
        WriteTlv(stream, "\0CONFIG", config);
        WriteTlv(stream, "\0IMAGE", theme);
    }
    sessionPath = await KachinaSessionPackage.CreateAsync(template,
        new LauncherUpdateInfo("v3.2.0", "新增功能\n修复问题\n<script>not executable</script>", "", "github"), work);
    var sessionBytes = await File.ReadAllBytesAsync(sessionPath);
    var configStart = Encoding.ASCII.GetBytes("MZ-test-native-code!IN\0\0\a\0CONFIG").Length;
    var configSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(sessionBytes.AsSpan(configStart, 4)));
    var sessionConfig = JsonNode.Parse(sessionBytes.AsSpan(configStart + 4, configSize))!;
    Check(sessionBytes.AsSpan(0, 19).SequenceEqual("MZ-test-native-code"u8), "native updater prefix remains unchanged");
    Check(sessionBytes.AsSpan(sessionBytes.Length - theme.Length).SequenceEqual(theme), "application image/theme remains unchanged");
    Check(sessionConfig["description"]!.GetValue<string>().Contains("新增功能\n修复问题"), "release notes embedded as JSON text, not executable HTML");
    Check(sessionConfig["programFilesPath"]!.GetValue<string>() == work, "temporary updater uses original absolute installation directory");
    Check(sessionConfig["source"]![0]!["uri"]!.GetValue<string>() == UpdateSourcePolicy.GetPackageUrl("cnb", "v3.2.0"), "CNB session source pins displayed version");
    Check(sessionConfig["source"]![1]!["uri"]!.GetValue<string>() == UpdateSourcePolicy.GetPackageUrl("github", "v3.2.0"), "GitHub alternative pins same version");
    using (var stream = new FileStream(template, FileMode.Append)) WriteTlv(stream, "\0INDEX", []);
    try { await KachinaSessionPackage.CreateAsync(template, null); throw new Exception("accepted indexed package"); }
    catch (InvalidDataException) { Check(true, "full installer/indexed package cannot be rewritten as updater template"); }
}
finally
{
    if (sessionPath is not null)
    {
        File.Delete(sessionPath);
        Directory.Delete(Path.GetDirectoryName(sessionPath)!);
    }
    File.Delete(Path.Combine(work, "template.exe"));
    Directory.Delete(work);
}

Console.WriteLine($"All {checks} checks passed.");

static void WriteTlv(Stream stream, string name, byte[] content)
{
    stream.Write("!IN\0"u8);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(size, (ushort)nameBytes.Length);
    stream.Write(size[..2]);
    stream.Write(nameBytes);
    BinaryPrimitives.WriteUInt32BigEndian(size, (uint)content.Length);
    stream.Write(size);
    stream.Write(content);
}
