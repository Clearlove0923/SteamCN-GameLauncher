using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class ScreenshotGalleryPage : Page
{
    public ScreenshotGalleryPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var games = CustomManifestService.Instance.GetSidebarItems();
        GameSelector.ItemsSource = games.Select(game => game.Name).ToList();
        var index = e.Parameter is string id ? games.ToList().FindIndex(game => game.Id == id) : -1;
        GameSelector.SelectedIndex = index >= 0 ? index : games.Count > 0 ? 0 : -1;
    }
}
