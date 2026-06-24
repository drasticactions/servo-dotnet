

namespace Servo;

[Flags]
public enum StorageTypes : byte
{
    Cookies = 1 << 0,
    Local = 1 << 1,
    Session = 1 << 2,
    All = Cookies | Local | Session,
}
