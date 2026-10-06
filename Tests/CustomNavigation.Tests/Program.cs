using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services;

// All mutations use a uniquely named test file; never load the user's actual settings.
var settingsPath = Path.Combine(AppContext.BaseDirectory, $"navigation-{Guid.NewGuid():N}.json");
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
try
{
    Check(SupportedGameRegistry.TryMatch(@"D:\Games\Genshin Impact Game\YuanShen.exe", null, out var genshin)
        && genshin.GameId == "genshin-impact" && genshin.ProviderId == "mihoyo"
        && genshin.ProviderOptions["gameBiz"].GetString() == "hk4e_cn"
        && genshin.ProviderOptions["videoOnly"].GetBoolean(),
        "game EXE resolves to the right provider with typed source options");
    Check(SupportedGameRegistry.TryMatch(@"D:\Games\Genshin Impact Game\unknown.exe", null, out var genshinFolder)
        && genshinFolder.GameId == "genshin-impact",
        "official install folder is used when EXE is unknown");
    Check(SupportedGameRegistry.TryMatch(@"D:\Games\Genshin Impact Game\HTGame.exe", null, out var preferredExe)
        && preferredExe.GameId == "genshin-impact",
        "recognized install folder takes precedence over a conflicting EXE name");
    Check(SupportedGameRegistry.TryMatch(@"E:\Games\原神\Genshin Impact Game\YuanShen.exe", null, out var nestedFolder)
        && nestedFolder.GameId == "genshin-impact"
        && SupportedGameRegistry.GetCacheFolderName(nestedFolder,
            @"E:\Games\原神\Genshin Impact Game\YuanShen.exe", null) == "Genshin Impact Game",
        "nearest recognized install folder wins for a nested game path");
    Check(SupportedGameRegistry.TryMatch(@"E:\Games\原神\Genshin Impact Game\MySteamLaunchBridge.exe", null,
            out var bridgeFolder)
        && bridgeFolder.GameId == "genshin-impact",
        "custom direct-launch bridge uses the game install folder");
    Check(SupportedGameRegistry.TryMatch(@"D:\Other\GenshinImpact.exe", null, out var globalGenshin)
        && globalGenshin.GameId == "genshin-impact",
        "documented Genshin executable is an EXE fallback alias");
    Check(SupportedGameRegistry.TryMatch(@"D:\Other\BH3.exe", null,
            out var bh3Executable) && bh3Executable.GameId == "honkai-impact-3rd",
        "game executable remains a standalone fallback keyword");
    Check(SupportedGameRegistry.TryMatch(@"D:\鸣潮\launcher.exe", null, out var kuroLauncher)
        && kuroLauncher.GameId == "wuthering-waves"
        && SupportedGameRegistry.TryMatch(@"D:\yysls\launcher.exe", null, out var neteaseLauncher)
        && neteaseLauncher.GameId == "where-winds-meet",
        "shared launcher executable is disambiguated by the install folder");
    Check(!SupportedGameRegistry.TryMatch(@"D:\Other\launcher.exe", null, out _),
        "shared launcher executable without a known install folder does not choose a provider");
    Check(!SupportedGameRegistry.TryMatch(@"D:\Other\EpicWebHelper.exe", null, out _)
        && !SupportedGameRegistry.TryMatch(@"D:\Other\crashpad_handler.exe", null, out _)
        && !SupportedGameRegistry.TryMatch(@"D:\Other\ZFGameBrowser.exe", null, out _)
        && !SupportedGameRegistry.TryMatch(@"D:\Other\WhereWindsMeetLaunchCapture.exe", null, out _),
        "helper and diagnostic executables are excluded from EXE keywords");
    Check(SupportedGameRegistry.TryMatch(@"D:\Other\unknown.exe", @"D:\Games\鸣潮", out var installFolder)
        && installFolder.GameId == "wuthering-waves",
        "explicit install directory is checked before EXE fallback");
    Check(SupportedGameRegistry.TryMatch(@"D:\NTE\NTEGame.exe", null, out var alternateExe)
        && alternateExe.GameId == "neverness-to-everness",
        "a second official executable identifies the same game");
    var sourceCases = new (string Path, string GameId, string ProviderId, string? GameBiz)[]
    {
        (@"D:\原神\YuanShen.exe", "genshin-impact", "mihoyo", "hk4e_cn"),
        (@"D:\绝区零\ZenlessZoneZero.exe", "zenless-zone-zero", "mihoyo", "nap_cn"),
        (@"D:\崩坏星穹铁道\StarRail.exe", "honkai-star-rail", "mihoyo", "hkrpg_cn"),
        (@"D:\崩坏3\BH3.exe", "honkai-impact-3rd", "mihoyo", "bh3_cn"),
        (@"D:\鸣潮\Client-Win64-Shipping.exe", "wuthering-waves", "kuro", null),
        (@"D:\明日方舟终末地\Endfield.exe", "arknights-endfield", "hypergryph", null),
        (@"D:\异环\HTGame.exe", "neverness-to-everness", "perfect-world", null),
        (@"D:\无限暖暖\InfinityNikki.exe", "infinity-nikki", "papergames", null),
        (@"D:\燕云十六声\wwm.exe", "where-winds-meet", "netease", null),
    };
    foreach (var (path, gameId, providerId, gameBiz) in sourceCases)
    {
        Check(SupportedGameRegistry.TryMatch(path, null, out var source)
            && source.GameId == gameId && source.ProviderId == providerId
            && source.NewsCategoryOrder.Count > 0
            && source.NewsCategoryOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                == source.NewsCategoryOrder.Count
            && (gameBiz is null || source.ProviderOptions["gameBiz"].GetString() == gameBiz),
            $"EXE routes {gameId} to its source and configured news categories");
        var unknownExe = Path.Combine(Path.GetDirectoryName(path)!, "unknown.exe");
        Check(SupportedGameRegistry.TryMatch(unknownExe, null, out var folderSource)
            && folderSource.GameId == gameId && folderSource.ProviderId == providerId,
            $"localized folder identifies {gameId} when EXE is unknown");
    }
    Check(!SupportedGameRegistry.TryMatch(@"D:\Games\unknown.exe", @"D:\Games\Other", out _),
        "unknown paths do not borrow a provider from the preset AppID");
    Check(SupportedGameRegistry.TryMatch(@"D:\无限暖暖\InfinityNikki.exe", null, out var nikki)
        && nikki.NewsCategoryLabels.TryGetValue("资讯", out var nikkiNewsLabel)
        && nikkiNewsLabel == "新闻"
        && nikki.NewsCategoryOrder.SequenceEqual(new[] { "公告", "新闻", "活动" }),
        "Infinity Nikki uses its configured official news labels and order");
    Check(SupportedGameRegistry.GetCacheFolderName(genshin, @"D:\Games\Genshin Impact Game\YuanShen.exe", @"D:\Games")
        == "Genshin Impact Game", "recognized game folder takes priority over the selected parent directory");
    Check(SupportedGameRegistry.GetCacheFolderName(alternateExe, @"D:\Other\NTEGame.exe", @"D:\Games\異環")
        == "異環", "localized install folder names the cache");
    Check(SupportedGameRegistry.TryGetProviderId("3513350", out var kuroProvider)
        && kuroProvider == "kuro", "Wuthering Waves AppId resolves to Kuro provider");
    Check(SupportedGameRegistry.TryGetProviderId(" 4162040 ", out var hoyoProvider)
        && hoyoProvider == "mihoyo", "provider lookup trims AppId and resolves miHoYo");
    Check(!SupportedGameRegistry.TryGetProviderId("not-supported", out var unknownProvider)
        && unknownProvider.Length == 0, "unknown AppId has no implicit provider");
    Check(SupportedGameRegistry.ResolveProviderId("4706890", "mihoyo-launcher", "preview")
            == "perfect-world"
        && SupportedGameRegistry.ResolveProviderId("3513350", "mihoyo-launcher", "preview")
            == "kuro",
        "known AppIds override stale provider IDs saved by older builds");
    Check(SupportedGameRegistry.AppIds.All(appId =>
            SupportedGameRegistry.GetProviderOptions(appId).TryGetValue("region", out var region)
            && region.GetString() == "cn"),
        "every built-in supported game explicitly requests Mainland China content");
    var endfieldOptions = SupportedGameRegistry.GetProviderOptions("4732690");
    Check(endfieldOptions["appCode"].GetString() == "6LL0KJuqHBVz33WK"
        && endfieldOptions["channel"].GetString() == "1"
        && endfieldOptions["subChannel"].GetString() == "1"
        && endfieldOptions["language"].GetString() == "zh-cn",
        "Endfield sends the complete Mainland China launcher tuple");
    Check(SupportedGameRegistry.GetProviderOptions("4162040")["gameBiz"].GetString() == "nap_cn"
        && SupportedGameRegistry.GetProviderOptions("1671200")["gameBiz"].GetString() == "bh3_cn",
        "HoYoPlay games pin their Mainland China game biz values");
    Check(SupportedGameRegistry.GetProviderOptions("not-supported").Count == 0,
        "unknown games do not receive implicit source-specific options");
    var store = new SettingsService(settingsPath);
    var service = new CustomManifestService(store);
    Check(service.GetInitialSidebarId() == null && service.GetSidebarItems().Count == 0,
        "fresh install has no default game");
    var builtInId = service.GetBuiltInId();
    Check(service.GetById(builtInId) != null, "legacy built-in preset retained without counting as a game");
    var first = service.Create("First game")!;
    Check(service.GetInitialSidebarId() == first.Id, "adding first game replaces empty state target");
    var second = service.Create("Second game")!;
    var third = service.Create("Third game")!;
    service.Select(second.Id);
    Check(new CustomManifestService(store).GetInitialSidebarId() == second.Id,
        "startup restores previously selected user game");
    Check(service.ReorderSidebarItems(new[] { third.Id, first.Id, second.Id })
        && service.GetSidebarItems().Select(p => p.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id })
        && service.GetInitialSidebarId() == second.Id, "drag order persists without changing current game");
    Check(!service.ReorderSidebarItems(new[] { first.Id, first.Id, second.Id })
        && service.GetSidebarItems().Select(p => p.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id }),
        "stale or duplicate drag order is rejected without data loss");
    service.Select(builtInId);
    Check(service.GetInitialSidebarId() == third.Id, "legacy selected preset falls back to first user game");
    Check(service.Delete(second.Id) == first.Id, "deleting last list entry selects previous user game");
    Check(service.Delete(third.Id) == first.Id, "deleting first list entry selects next user game");
    Check(service.Delete(first.Id) == builtInId && service.GetInitialSidebarId() == null
        && service.GetSidebarItems().Count == 0, "deleting last game returns to empty state");
    var readded = service.Create("Added again")!;
    Check(service.GetInitialSidebarId() == readded.Id, "game can be added after deleting all games");
    readded.HomeLaunchModeId = HomeLaunchModeIds.DirectCn;
    readded.ScreenshotDirectoryPath = @"D:\Screenshots\AddedAgain";
    Check(service.Update(readded)
        && service.GetById(readded.Id)?.HomeLaunchModeId == HomeLaunchModeIds.DirectCn
        && service.GetById(readded.Id)?.ScreenshotDirectoryPath == @"D:\Screenshots\AddedAgain",
        "home launch mode and user-selected screenshot path persist per game");

    var screenshotRoot = Path.Combine(AppContext.BaseDirectory, $"screenshots-{Guid.NewGuid():N}");
    Directory.CreateDirectory(screenshotRoot);
    try
    {
        var older = Path.Combine(screenshotRoot, "older.jpg");
        var newer = Path.Combine(screenshotRoot, "newer.png");
        File.WriteAllBytes(older, [1]);
        File.WriteAllBytes(newer, [2]);
        File.WriteAllText(Path.Combine(screenshotRoot, "ignored.txt"), "not an image");
        File.SetLastWriteTime(older, new DateTime(2026, 1, 1, 12, 0, 0));
        File.SetLastWriteTime(newer, new DateTime(2026, 1, 2, 12, 0, 0));
        var catalog = new ScreenshotCatalogService();
        var newestFirst = await catalog.GetAsync(screenshotRoot, newestFirst: true);
        var oldestFirst = await catalog.GetAsync(screenshotRoot, newestFirst: false);
        Check(newestFirst.Select(item => item.FileName).SequenceEqual(new[] { "newer.png", "older.jpg" })
            && oldestFirst.Select(item => item.FileName).SequenceEqual(new[] { "older.jpg", "newer.png" }),
            "screenshot catalog filters non-images and honors both sort orders");
    }
    finally
    {
        Directory.Delete(screenshotRoot, recursive: true);
    }
    store.Save(new AppSettings
    {
        CurrentCustomManifest = new CustomManifestPreset
        {
            AppId = "123",
            GameDisplayName = "Legacy data",
            LaunchArguments = "-dx11"
        }
    });
    Check(service.GetInitialSidebarId() == null
        && service.GetAll().Any(p => p.AppId == "123" && p.LaunchArguments == "-dx11"),
        "old settings migration preserves data and launch arguments without introducing a default sidebar game");
    Check(service.GetAll().All(p => p.HomeLaunchModeId == HomeLaunchModeIds.SteamCn),
        "old settings default to Steam CN home launch mode");
    Check(!store.Load().Appearance.Enabled && store.Load().Appearance.ShowHomeAnimation
        && store.Load().Appearance.ShowHomeNews && store.Load().Appearance.SidebarOpacity == 1d,
        "legacy settings default to original appearance, visible home content, and an opaque sidebar");
    Check(store.Load().HomeCacheMaximumMegabytes == 1024
        && store.Load().HomeCacheRetentionDays == 30,
        "legacy settings receive safe default home-cache limits");
    store.Update(s =>
    {
        s.Appearance.Enabled = true;
        s.Appearance.ShowHomeNews = false;
        s.Appearance.SelectedImage = "first.png";
        s.Appearance.Current.Opacity = 0.35;
        s.Appearance.SelectedImage = "second.jpg";
        s.Appearance.Current.OverlayOpacity = 0.7;
        s.Appearance.Current.Stretch = "Uniform";
    });
    var appearance = store.Load().Appearance;
    Check(appearance.Enabled && !appearance.ShowHomeNews && appearance.SelectedImage == "second.jpg"
        && appearance.Current.OverlayOpacity == 0.7 && appearance.Current.Stretch == "Uniform"
        && appearance.Images["first.png"].Opacity == 0.35,
        "background selection and independent per-image options survive restart");
    appearance.InitializePerPageSettings();
    appearance.UsePerPageSettings = true;
    var homeAppearance = appearance.GetEffective(AppearancePageIds.Home);
    var settingsAppearance = appearance.GetEffective(AppearancePageIds.Settings);
    Check(homeAppearance.SelectedImage == appearance.SelectedImage
        && settingsAppearance.CardOpacity == appearance.CardOpacity
        && appearance.Pages.Count == AppearancePageIds.All.Count,
        "per-page mode initially copies every unified appearance setting");
    homeAppearance.CardOpacity = 0.2;
    homeAppearance.SidebarOpacity = 0.45;
    homeAppearance.ShowHomeAnimation = false;
    homeAppearance.Current.Opacity = 0.15;
    settingsAppearance.CardOpacity = 0.8;
    store.Update(s => s.Appearance = appearance);
    var perPageAppearance = store.Load().Appearance;
    Check(perPageAppearance.GetEffective(AppearancePageIds.Home).CardOpacity == 0.2
        && perPageAppearance.GetEffective(AppearancePageIds.Home).SidebarOpacity == 0.45
        && !perPageAppearance.GetEffective(AppearancePageIds.Home).ShowHomeAnimation
        && perPageAppearance.GetEffective(AppearancePageIds.Home).Current.Opacity == 0.15
        && perPageAppearance.GetEffective(AppearancePageIds.Settings).CardOpacity == 0.8
        && perPageAppearance.GetEffective(AppearancePageIds.Settings).Current.Opacity != 0.15,
        "per-page appearance adjustments remain independent after restart");
    service.Create("Appearance regression");
    Check(store.Load().Appearance.SelectedImage == "second.jpg", "game updates preserve appearance settings");
    var gameCount = service.GetAll().Count;
    store.Update(s => s.Appearance.Enabled = false);
    Check(service.GetAll().Count == gameCount && store.Load().Appearance.Images.Count == 2,
        "disabling background preserves games and saved image options");
    Console.WriteLine($"All {checks} checks passed.");
}
finally
{
    if (File.Exists(settingsPath)) File.Delete(settingsPath);
}
