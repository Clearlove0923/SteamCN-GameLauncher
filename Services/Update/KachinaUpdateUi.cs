using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>为 CNB 完整包下载提供可取消的原生进度窗口；其他来源仍使用 Kachina 自身界面。</summary>
public static class KachinaUpdateUi
{
    public static async Task<string?> ShowAsync(
        XamlRoot xamlRoot,
        string? sourceId,
        LauncherUpdateInfo? update = null)
    {
        var normalizedSource = UpdateSourcePolicy.Normalize(sourceId);
        if (normalizedSource != UpdateSourceIds.Cnb)
            return await KachinaUpdateService.Instance.ShowAsync(normalizedSource, update);

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
            Title = "正在下载更新",
            Content = content,
            CloseButtonText = "取消",
            XamlRoot = xamlRoot
        };

        using var cancellation = new CancellationTokenSource();
        var progress = new Progress<UpdatePackageDownloadProgress>(value =>
        {
            var receivedMb = value.BytesReceived / 1024d / 1024d;
            if (value.TotalBytes is > 0)
            {
                var totalMb = value.TotalBytes.Value / 1024d / 1024d;
                var percent = Math.Clamp(value.BytesReceived * 100d / value.TotalBytes.Value, 0, 100);
                progressBar.IsIndeterminate = false;
                progressBar.Value = percent;
                status.Text = $"正在从 CNB 顺序下载完整更新包：{receivedMb:F1} / {totalMb:F1} MB（{percent:F0}%）";
            }
            else
            {
                status.Text = $"正在从 CNB 顺序下载完整更新包：{receivedMb:F1} MB";
            }
        });

        var dialogTask = dialog.ShowAsync().AsTask();
        var launchTask = KachinaUpdateService.Instance.ShowAsync(
            normalizedSource, update, progress, cancellation.Token);
        var completed = await Task.WhenAny(dialogTask, launchTask);
        if (completed == dialogTask)
        {
            cancellation.Cancel();
            await launchTask;
            return null;
        }

        var error = await launchTask;
        dialog.Hide();
        await dialogTask;
        return error;
    }
}
