using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamCNGameLauncher.Models;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher.Views.Pages;

public sealed partial class GameLaunchTestPage : Page
{
    private readonly SettingsService _settingsService = new();
    private readonly CustomManifestService _manifestService = CustomManifestService.Instance;
    private readonly SteamLaunchService _launchService = new();

    public GameLaunchTestPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var games = _manifestService.GetSidebarItems().ToList();
        cmbGames.ItemsSource = games;

        var currentId = _manifestService.GetCurrentId();
        cmbGames.SelectedItem = games.FirstOrDefault(game =>
            string.Equals(game.Id, currentId, StringComparison.OrdinalIgnoreCase))
            ?? games.FirstOrDefault();

        if (games.Count == 0)
        {
            ShowStatus(new(SteamLaunchStatus.InvalidAppId,
                "还没有已添加的游戏，请先通过侧边栏添加并完成配置。"));
            btnLaunch.IsEnabled = false;
        }
        else
        {
            RefreshStatus();
        }
    }

    private CustomManifestPreset? SelectedGame => cmbGames.SelectedItem as CustomManifestPreset;

    private void Game_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            txtAppId.Text = string.IsNullOrWhiteSpace(game.AppId) ? "—" : game.AppId;
            txtGameExe.Text = string.IsNullOrWhiteSpace(game.ClientExePath) ? "—" : game.ClientExePath;
        }
        else
        {
            txtAppId.Text = "—";
            txtGameExe.Text = "—";
        }
        RefreshStatus();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var game = SelectedGame;
        if (game == null)
        {
            btnLaunch.IsEnabled = false;
            return;
        }

        var result = _launchService.CheckReady(_settingsService.Load(), game);
        btnLaunch.IsEnabled = result.IsSuccess;
        ShowStatus(result);
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game == null) return;

        btnLaunch.IsEnabled = false;
        var result = _launchService.Launch(_settingsService.Load(), game);
        ShowStatus(result);

        if (!result.IsSuccess)
            await ShowInfoAsync(result.Message);
        else
            btnLaunch.IsEnabled = true;
    }

    private void ShowStatus(SteamLaunchResult result)
    {
        statusInfoBar.Title = result.Status switch
        {
            SteamLaunchStatus.Ready => "可以开始游戏",
            SteamLaunchStatus.Started => "已交给 Steam 启动",
            SteamLaunchStatus.SteamNotRunning => "请打开并登录 Steam",
            _ => "暂时无法启动",
        };
        statusInfoBar.Message = result.Message;
        statusInfoBar.Severity = result.Status switch
        {
            SteamLaunchStatus.Ready or SteamLaunchStatus.Started => InfoBarSeverity.Success,
            SteamLaunchStatus.SteamNotRunning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
