using System.Windows;
using EjectScope.Core;

namespace EjectScope.App;

public partial class App : Application
{
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--handle-path-resolver"])
        {
            HandlePathResolverHost.Run();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        _tray = new TrayController();
        _tray.ShowWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
