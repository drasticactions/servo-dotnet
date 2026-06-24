using System.Runtime.InteropServices;

namespace Servo;

public sealed class WebResourceLoadEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    public string Url { get; }
    public string Method { get; }
    public bool IsForMainFrame { get; }
    public bool IsRedirect { get; }

    internal WebResourceLoadEventArgs(string url, string method, bool isForMainFrame, bool isRedirect, nuint handle)
    {
        Url = url;
        Method = method;
        IsForMainFrame = isForMainFrame;
        IsRedirect = isRedirect;
        _handle = handle;
    }

    /// <summary>
    /// Let the load proceed normally (do not intercept).
    /// </summary>
    public void Allow()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.web_resource_load_dismiss(_handle);
    }

    /// <summary>
    /// Intercept the load and respond with the given status code and body.
    /// </summary>
    public unsafe void Intercept(ushort statusCode, byte[]? body = null)
    {
        if (_responded) return;
        _responded = true;
        if (body != null && body.Length > 0)
        {
            fixed (byte* pBody = body)
            {
                ServoNative.web_resource_load_intercept(_handle, statusCode, pBody, (nuint)body.Length);
            }
        }
        else
        {
            ServoNative.web_resource_load_intercept(_handle, statusCode, null, 0);
        }
    }

    /// <summary>
    /// Intercept the load and respond with 200 OK and the given string body.
    /// </summary>
    public void Intercept(string body, string contentType = "text/html")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        Intercept(200, bytes);
    }

    /// <summary>
    /// Cancel the load (triggers a network error).
    /// </summary>
    public void Cancel()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.web_resource_load_cancel(_handle);
    }
}
