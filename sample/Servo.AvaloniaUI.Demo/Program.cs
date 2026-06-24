using System;
using System.IO;
using Avalonia;
using Servo.AvaloniaUI;

namespace Servo.AvaloniaUI.Demo;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseServo(ResolveResourcePath())
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static string? ResolveResourcePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources");
        return Directory.Exists(path) ? path : null;
    }
}
