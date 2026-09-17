using SteamCNGameLauncher.Models;
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
    Check(service.Update(readded)
        && service.GetById(readded.Id)?.HomeLaunchModeId == HomeLaunchModeIds.DirectCn,
        "home launch mode persists per game");
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
        && store.Load().Appearance.ShowHomeNews,
        "legacy settings default to original appearance with home animation and news visible");
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
    homeAppearance.ShowHomeAnimation = false;
    homeAppearance.Current.Opacity = 0.15;
    settingsAppearance.CardOpacity = 0.8;
    store.Update(s => s.Appearance = appearance);
    var perPageAppearance = store.Load().Appearance;
    Check(perPageAppearance.GetEffective(AppearancePageIds.Home).CardOpacity == 0.2
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
