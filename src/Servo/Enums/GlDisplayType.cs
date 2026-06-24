

namespace Servo;

public enum GlDisplayType : byte
{
    Egl = 0,
    X11 = 1,
    Wayland = 2,
    Headless = 3,
    Unknown = 4,
}
