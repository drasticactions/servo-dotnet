using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Servo.AvaloniaUI;

public static class ServoAppBuilderExtensions
{
    public static global::Avalonia.AppBuilder UseServo(
        this global::Avalonia.AppBuilder builder,
        string? resourcePath = null,
        ProtocolRegistry? protocolRegistry = null)
    {
        builder.AfterSetup(b =>
        {
            var engine = new ServoEngine(resourcePath, protocolRegistry);
            var wakePending = 0;
            engine.EventLoopWaker = () =>
            {
                if (Interlocked.Exchange(ref wakePending, 1) == 1)
                    return;
                Dispatcher.UIThread.Post(() =>
                {
                    Volatile.Write(ref wakePending, 0);
                    if (engine.IsDisposed)
                        return;
                    engine.SpinEventLoop();
                }, DispatcherPriority.Render);
            };
            ServoLocator.Engine = engine;

            Dispatcher.UIThread.Post(() =>
            {
                if (b.Instance?.ApplicationLifetime is IControlledApplicationLifetime controlled)
                {
                    controlled.Exit += (_, _) =>
                    {
                        if (!engine.IsDisposed)
                            engine.Dispose();
                    };
                }
            });
        });

        return builder;
    }
}
