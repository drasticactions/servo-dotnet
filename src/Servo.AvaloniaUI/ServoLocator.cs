using System;

namespace Servo.AvaloniaUI;

public static class ServoLocator
{
    private static ServoEngine? _engine;

    public static ServoEngine Engine
    {
        get => _engine ?? throw new InvalidOperationException(
            "Servo engine not initialized. Call .UseServo() in your AppBuilder pipeline, " +
            "or set ServoLocator.Engine manually before creating any ServoWebViewControl.");
        set => _engine = value;
    }

    public static bool IsInitialized => _engine != null;
}
