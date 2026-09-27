using System.Drawing;
using System.Windows.Forms;
using Microsoft.UI.Windowing;
using SteamCNGameLauncher.Services;

namespace SteamCNGameLauncher;

public sealed partial class MainWindow
{
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private Icon? _trayImage;
    private bool _exitFromTray;

    private void InitializeTrayIcon()
    {
        // 托盘菜单使用与 EXE 相同的 ICO；初始化失败时关闭窗口仍按普通退出处理。
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "SteamCN-GameLauncher.ico");
            _trayImage = new Icon(iconPath);
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("打开主窗口", null, (_, _) => RestoreFromTray());
            _trayMenu.Items.Add("退出", null, (_, _) => ExitFromTray());
            _trayIcon = new NotifyIcon
            {
                Icon = _trayImage,
                Text = AppInfo.WindowTitle,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.MouseClick += (_, args) =>
            {
                if (args.Button == MouseButtons.Left) RestoreFromTray();
            };
            Closed += (_, _) => DisposeTrayIcon();
        }
        catch (Exception ex)
        {
            DisposeTrayIcon();
            LogService.Instance.AddLog($"[窗口] 托盘图标初始化失败，将按常规方式关闭：{ex.Message}");
        }
    }

    private void RestoreFromTray()
    {
        // WinForms 托盘事件不保证在 WinUI 的 UI 线程触发，窗口操作统一派发回来。
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RestoreFromTray);
            return;
        }

        if (_exitFromTray || _appWindow is null) return;
        if (_appWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        _appWindow.Show();
        Activate();
        LogService.Instance.AddLog("[窗口] 已从系统托盘恢复");
    }

    private void ExitFromTray()
    {
        // 只允许托盘“退出”设置退出标志；标题栏关闭则由窗口事件改为隐藏到托盘。
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(ExitFromTray);
            return;
        }

        if (_exitFromTray) return;
        _exitFromTray = true;
        Close();
    }

    private void DisposeTrayIcon()
    {
        // 应用退出前撤掉 NotifyIcon，否则通知区域可能留下暂时无法响应的旧图标。
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayImage?.Dispose();
        _trayImage = null;
    }
}
