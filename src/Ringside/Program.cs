using System;
using Avalonia;
using Avalonia.Media;
using Ringside.Platform;
using Velopack;

namespace Ringside;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Claude Code runs this for its status line after every reply (macOS and Linux;
        // Windows uses the native ringside-statusline.exe). Nothing else may start first.
        if (args.Length > 0 && args[0] == "--statusline")
            return StatusLineBridge.Run();

        // Must run before the UI: the installer calls back into the app through it on
        // install, update and uninstall.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Cleanup.Run(quitRunningCopy: true))
            .Run();

        // Already running: bring that one forward instead.
        if (!SingleInstance.Claim())
        {
            SingleInstance.Signal("activate");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            // A menu bar app on macOS: no Dock icon, no app menu of its own.
            .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true })
            .With(new Win32PlatformOptions { OverlayPopups = false })
            // Matches StartupWMClass in the AppImage's desktop file, so docks group it right.
            .With(new X11PlatformOptions { WmClass = "Ringside" })
            .LogToTrace();
}
