using System;
using Velopack;

namespace Ringside;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: the installer calls back into the app through it on install,
        // update and uninstall.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Uninstall.Cleanup())
            .Run();

        if (args.Length == 2 && args[0] == "--render-screenshots")
            return Screenshots.Render(args[1]);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
