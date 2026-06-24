namespace Servo;

public interface IProtocolHandler
{
    ProtocolResponse? Load(string url);

    bool IsFetchable => false;

    bool IsSecure => false;

    IReadOnlyList<string> PrivilegedPaths => [];
}
