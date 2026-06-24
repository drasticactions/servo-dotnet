using System.Runtime.InteropServices;

namespace Servo;

public sealed class AuthenticationRequestEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    /// <summary>
    /// The URL that triggered this authentication request.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Whether this authentication request is for a proxy server.
    /// </summary>
    public bool ForProxy { get; }

    internal AuthenticationRequestEventArgs(string url, bool forProxy, nuint handle)
    {
        Url = url;
        ForProxy = forProxy;
        _handle = handle;
    }

    /// <summary>
    /// Respond with credentials.
    /// </summary>
    public unsafe void Authenticate(string username, string password)
    {
        if (_responded) return;
        _responded = true;
        var pUser = Marshal.StringToCoTaskMemUTF8(username);
        var pPass = Marshal.StringToCoTaskMemUTF8(password);
        try
        {
            ServoNative.authentication_request_authenticate(_handle, (byte*)pUser, (byte*)pPass);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pUser);
            Marshal.FreeCoTaskMem(pPass);
        }
    }

    /// <summary>
    /// Dismiss without providing credentials.
    /// </summary>
    public void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.authentication_request_dismiss(_handle);
    }
}
