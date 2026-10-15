using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Ringside;

public partial class App : Application
{
    /// <summary>Set by the image renderer: styles only, no tray, no windows of its own.</summary>
    public static bool RenderOnly;

    public static WindowIcon? WindowIcon { get; private set; }

    Controller? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try { WindowIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://Ringside/Assets/ringside.png"))); }
        catch (Exception e) { Log.Write("icon load failed: " + e.Message); }

        if (!RenderOnly && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _controller = new Controller(desktop);
            _controller.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
