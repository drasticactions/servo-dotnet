

namespace Servo;

[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    AltGraph = 0x2,
    CapsLock = 0x4,
    Control = 0x8,
    Fn = 0x10,
    FnLock = 0x20,
    Meta = 0x40,
    NumLock = 0x80,
    ScrollLock = 0x100,
    Shift = 0x200,
    Symbol = 0x400,
    SymbolLock = 0x800,
    Hyper = 0x1000,
    Super = 0x2000,
}
