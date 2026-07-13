using System.Windows;
using Forms = System.Windows.Forms;

namespace EjectScope.App;

public sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly MainWindow _window;

    public TrayController()
    {
        _window = new MainWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("スキャン", null, async (_, _) => await _window.ScanAsync());
        menu.Items.Add("再スキャン", null, async (_, _) => await _window.ScanAsync());
        menu.Items.Add("設定", null, (_, _) => _window.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        _icon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "EjectScope — 取り外しブロッカー解析", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => ShowWindow();
        _window.Closed += (_, _) => Application.Current.Shutdown();
    }

    public void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _window.AllowClose = true;
        _window.Close();
        Application.Current.Shutdown();
    }

    public void Dispose() => _icon.Dispose();
}
