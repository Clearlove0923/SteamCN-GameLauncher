using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>更新相关原生弹窗共用的角色装饰与双栏布局。</summary>
internal static class UpdateDialogVisuals
{
    private const string CharacterAssetUri =
        "ms-appx:///Assets/Icons/character-cutout.png";

    public static ContentDialog CreateReleaseDialog(
        XamlRoot xamlRoot,
        string version,
        string releaseNotes)
    {
        var notes = new TextBlock
        {
            Text = releaseNotes,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var notesScroller = new ScrollViewer
        {
            Content = notes,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = new ContentDialog
        {
            Content = CreateTwoColumnContent(
                $"发现新版本 {version}", notesScroller, 360, VerticalAlignment.Center),
            PrimaryButtonText = "立即更新",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        ApplyDialogWidth(dialog);
        return dialog;
    }

    public static (ContentDialog Dialog, TextBlock Status, ProgressBar ProgressBar)
        CreateDownloadProgressDialog(XamlRoot xamlRoot)
    {
        var status = new TextBlock
        {
            Text = "正在连接 CNB，准备下载完整更新包……",
            TextWrapping = TextWrapping.Wrap
        };
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Minimum = 0,
            Maximum = 100
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(status);
        content.Children.Add(progressBar);
        var dialog = new ContentDialog
        {
            Content = CreateTwoColumnContent(
                "正在下载更新", content, 240),
            CloseButtonText = "取消",
            XamlRoot = xamlRoot
        };
        ApplyDialogWidth(dialog);
        return (dialog, status, progressBar);
    }

    private static Grid CreateTwoColumnContent(
        string heading,
        UIElement body,
        double minimumHeight,
        VerticalAlignment characterVerticalAlignment = VerticalAlignment.Bottom)
    {
        var layout = new Grid
        {
            Width = 680,
            MinHeight = minimumHeight,
            ColumnSpacing = 24
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var character = new Image
        {
            Source = new BitmapImage(new Uri(CharacterAssetUri)),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = characterVerticalAlignment,
            MaxHeight = minimumHeight
        };
        AutomationProperties.SetName(character, "更新界面人物装饰");
        Grid.SetColumn(character, 0);
        layout.Children.Add(character);

        var right = new StackPanel
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        right.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        right.Children.Add(body);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);

        return layout;
    }

    private static void ApplyDialogWidth(ContentDialog dialog)
    {
        dialog.Resources["ContentDialogMinWidth"] = 720d;
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
    }
}
