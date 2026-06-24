namespace Servo;

public sealed class CreateNewWebViewRequestEventArgs : EventArgs
{
    private bool _responded;

    /// <summary>
    /// The native handle for the CreateNewWebViewRequest.
    /// Pass this to a <see cref="ServoWebView"/> constructor or
    /// set it as the PendingCreateNewWebViewRequest on a ServoWebViewControl.
    /// </summary>
    public nuint RequestHandle { get; }

    internal CreateNewWebViewRequestEventArgs(nuint handle)
    {
        RequestHandle = handle;
    }

    /// <summary>
    /// Mark this request as handled. Call this after you have used
    /// the <see cref="RequestHandle"/> to build a new WebView.
    /// </summary>
    public void MarkHandled()
    {
        _responded = true;
    }

    /// <summary>
    /// Dismiss the request without creating a new WebView.
    /// </summary>
    public void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.create_new_webview_dismiss(RequestHandle);
    }

    public bool IsHandled => _responded;
}
