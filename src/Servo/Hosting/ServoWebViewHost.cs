using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Servo.Hosting;

/// <summary>
/// Framework-agnostic core that hosts a single <see cref="ServoWebView"/> on behalf of a concrete
/// UI control.
/// </summary>
public sealed class ServoWebViewHost : IDisposable, INotifyPropertyChanged
{
    private readonly IServoHostPlatform _platform;

    private ServoWebView? _webView;
    private string? _pendingUrl;
    private bool _renderPending;
    private double _lastScaling = 1.0;
    private bool _imeComposing;
    private string _markedText = string.Empty;

    public ServoWebViewHost(IServoHostPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <summary>The underlying web view, or <c>null</c> before <see cref="Initialize"/> / after <see cref="Dispose"/>.</summary>
    public ServoWebView? WebView => _webView;

    /// <summary>Whether the web view has been created.</summary>
    public bool IsInitialized => _webView != null;

    // ----- Observable browser state (INotifyPropertyChanged) -----

    public event PropertyChangedEventHandler? PropertyChanged;

    private string? _url;
    private string? _pageTitle;
    private bool _isLoading;
    private LoadStatus _status;
    private bool _canGoBack;
    private bool _canGoForward;
    private string? _statusText;

    /// <summary>The currently committed document URL.</summary>
    public string? Url { get => _url; private set => SetProperty(ref _url, value); }

    /// <summary>The current document title.</summary>
    public string? PageTitle { get => _pageTitle; private set => SetProperty(ref _pageTitle, value); }

    /// <summary>Whether a load is in progress (i.e. <see cref="Status"/> is not <see cref="LoadStatus.Complete"/>).</summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    /// <summary>The most recent load status reported by the engine.</summary>
    public LoadStatus Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Whether the session has a back entry to navigate to.</summary>
    public bool CanGoBack { get => _canGoBack; private set => SetProperty(ref _canGoBack, value); }

    /// <summary>Whether the session has a forward entry to navigate to.</summary>
    public bool CanGoForward { get => _canGoForward; private set => SetProperty(ref _canGoForward, value); }

    /// <summary>The current status-bar text (e.g. a hovered link target), or <c>null</c>.</summary>
    public string? StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event EventHandler<UrlChangedEventArgs>? Navigated;
    public event EventHandler<TitleChangedEventArgs>? TitleChanged;
    public event EventHandler<LoadStatusChangedEventArgs>? LoadStatusChanged;
    public event EventHandler<CursorChangedEventArgs>? CursorChanged;
    public event EventHandler? FaviconChanged;
    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
    public event EventHandler<CrashedEventArgs>? Crashed;
    public event EventHandler<ConsoleMessageEventArgs>? ConsoleMessage;
    public event EventHandler<StatusTextChangedEventArgs>? StatusTextChanged;
    public event EventHandler? TraversalCompleted;
    public event EventHandler<MoveToRequestEventArgs>? MoveToRequested;
    public event EventHandler<ResizeToRequestEventArgs>? ResizeToRequested;
    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;
    public event EventHandler<UnloadRequestEventArgs>? UnloadRequested;
    public event EventHandler<WebResourceLoadEventArgs>? WebResourceLoadRequested;
    public event EventHandler<AlertRequestEventArgs>? AlertRequested;
    public event EventHandler<ConfirmRequestEventArgs>? ConfirmRequested;
    public event EventHandler<PromptRequestEventArgs>? PromptRequested;
    public event EventHandler<SelectElementRequestEventArgs>? SelectElementRequested;
    public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;
    public event EventHandler<CreateNewWebViewRequestEventArgs>? CreateNewWebViewRequested;
    public event EventHandler<AuthenticationRequestEventArgs>? AuthenticationRequested;
    public event EventHandler<ProtocolHandlerRequestEventArgs>? ProtocolHandlerRequested;
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;
    public event EventHandler<NotificationEventArgs>? NotificationRequested;
    public event EventHandler<BluetoothDeviceSelectionEventArgs>? BluetoothDeviceSelectionRequested;
    public event EventHandler<FilePickerRequestEventArgs>? FilePickerRequested;
    public event EventHandler<ColorPickerRequestEventArgs>? ColorPickerRequested;
    public event EventHandler<InputMethodEventArgs>? InputMethodRequested;
    public event EventHandler? HideEmbedderControlRequested;

    /// <summary>
    /// Create the web view from the engine and start it. Safe to call once; later calls are no-ops.
    /// </summary>
    /// <param name="engine">The engine that owns the new browsing context.</param>
    /// <param name="initialUrl">
    /// The URL to open. Falls back to any URL buffered by an earlier <see cref="Navigate(string)"/>.
    /// </param>
    public void Initialize(ServoEngine engine, string? initialUrl = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        InitializeCore(rc => new ServoWebView(engine, rc, initialUrl ?? _pendingUrl));
    }

    /// <summary>
    /// Create the web view from a pending <see cref="CreateNewWebViewRequestEventArgs"/> handle
    /// and start it.
    /// </summary>
    public void InitializeFromCreateRequest(nuint requestHandle) =>
        InitializeCore(rc => new ServoWebView(rc, requestHandle));

    private void InitializeCore(Func<RenderingContext, ServoWebView> createWebView)
    {
        if (_webView != null) return;

        var scaling = _platform.GetScaling();
        _lastScaling = scaling;
        var (pw, ph) = ComputePixelSize(scaling);

        var renderingContext = _platform.CreateRenderingContext(pw, ph);
        _webView = createWebView(renderingContext);
        _pendingUrl = null;
        _webView.SetHidpiScale((float)scaling);
        _webView.NotifyThemeChange(_platform.Theme);
        if (_pendingZoom != 1.0f) _webView.PageZoom = _pendingZoom;

        WireEvents(_webView);

        _webView.Show();
        _webView.Focus();
    }

    private void WireEvents(ServoWebView wv)
    {
        wv.NewFrameReady += (_, _) =>
        {
            if (_renderPending)
                return;
            _renderPending = true;
            _platform.PostRender(RenderFrame);
        };

        wv.UrlChanged += (_, e) => Post(() => { Url = e.Url; Navigated?.Invoke(this, e); });
        wv.TitleChanged += (_, e) => Post(() => { PageTitle = e.Title; TitleChanged?.Invoke(this, e); });
        wv.LoadStatusChanged += (_, e) => Post(() =>
        {
            Status = e.Status;
            IsLoading = e.Status != LoadStatus.Complete;
            LoadStatusChanged?.Invoke(this, e);
        });
        wv.CursorChanged += (_, e) => Post(() => CursorChanged?.Invoke(this, e));
        wv.FaviconChanged += (_, _) => Post(() => FaviconChanged?.Invoke(this, EventArgs.Empty));
        wv.HistoryChanged += (_, e) => Post(() =>
        {
            CanGoBack = _webView?.CanGoBack ?? false;
            CanGoForward = _webView?.CanGoForward ?? false;
            HistoryChanged?.Invoke(this, e);
        });
        wv.Crashed += (_, e) => Post(() => Crashed?.Invoke(this, e));
        wv.WebViewConsoleMessage += (_, e) => Post(() => ConsoleMessage?.Invoke(this, e));
        wv.StatusTextChanged += (_, e) => Post(() => { StatusText = e.StatusText; StatusTextChanged?.Invoke(this, e); });
        wv.TraversalCompleted += (_, _) => Post(() => TraversalCompleted?.Invoke(this, EventArgs.Empty));
        wv.MoveToRequested += (_, e) => Post(() => MoveToRequested?.Invoke(this, e));
        wv.ResizeToRequested += (_, e) => Post(() => ResizeToRequested?.Invoke(this, e));
        wv.NotificationRequested += (_, e) => Post(() => NotificationRequested?.Invoke(this, e));
        wv.InputMethodRequested += (_, e) => Post(() => InputMethodRequested?.Invoke(this, e));
        wv.HideEmbedderControlRequested += (_, _) => Post(() =>
        {
            ResetComposition();
            HideEmbedderControlRequested?.Invoke(this, EventArgs.Empty);
        });

        wv.AlertRequested += (_, e) => Post(() => { if (AlertRequested != null) AlertRequested.Invoke(this, e); else e.Dismiss(); });
        wv.ConfirmRequested += (_, e) => Post(() => { if (ConfirmRequested != null) ConfirmRequested.Invoke(this, e); else e.Cancel(); });
        wv.PromptRequested += (_, e) => Post(() => { if (PromptRequested != null) PromptRequested.Invoke(this, e); else e.Cancel(); });
        wv.SelectElementRequested += (_, e) => Post(() => { if (SelectElementRequested != null) SelectElementRequested.Invoke(this, e); else e.Dismiss(); });
        wv.ContextMenuRequested += (_, e) => Post(() => { if (ContextMenuRequested != null) ContextMenuRequested.Invoke(this, e); else e.Dismiss(); });
        wv.CreateNewWebViewRequested += (_, e) => Post(() => { if (CreateNewWebViewRequested != null) CreateNewWebViewRequested.Invoke(this, e); else e.Dismiss(); });
        wv.AuthenticationRequested += (_, e) => Post(() => { if (AuthenticationRequested != null) AuthenticationRequested.Invoke(this, e); else e.Dismiss(); });
        wv.ProtocolHandlerRequested += (_, e) => Post(() => { if (ProtocolHandlerRequested != null) ProtocolHandlerRequested.Invoke(this, e); else e.Deny(); });
        wv.PermissionRequested += (_, e) => Post(() => { if (PermissionRequested != null) PermissionRequested.Invoke(this, e); else e.Deny(); });
        wv.BluetoothDeviceSelectionRequested += (_, e) => Post(() => { if (BluetoothDeviceSelectionRequested != null) BluetoothDeviceSelectionRequested.Invoke(this, e); else e.Cancel(); });
        wv.FilePickerRequested += (_, e) => Post(() => { if (FilePickerRequested != null) FilePickerRequested.Invoke(this, e); else e.Dismiss(); });
        wv.ColorPickerRequested += (_, e) => Post(() => { if (ColorPickerRequested != null) ColorPickerRequested.Invoke(this, e); else e.Dismiss(); });

        wv.NavigationRequested += (_, e) => NavigationRequested?.Invoke(this, e);
        wv.UnloadRequested += (_, e) => UnloadRequested?.Invoke(this, e);
        wv.WebResourceLoadRequested += (_, e) =>
        {
            if (WebResourceLoadRequested != null) WebResourceLoadRequested.Invoke(this, e);
            else e.Allow();
        };
    }

    private void Post(Action action) => _platform.Post(action);

    /// <summary>Paint the current frame and present it. Called on every new frame and after a resize.</summary>
    public void RenderFrame()
    {
        // Cleared before painting so a frame arriving mid-paint schedules the next one.
        _renderPending = false;
        if (_webView == null) return;
        _webView.Paint();
        _platform.PresentFrame();
    }

    /// <summary>Recompute the surface size, resize the web view, and push a hi-dpi change if the scale moved.</summary>
    public void Resize()
    {
        if (_webView == null) return;
        var scaling = _platform.GetScaling();
        var (pw, ph) = ComputePixelSize(scaling);
        if (pw == 0 || ph == 0) return;

        _webView.Resize(pw, ph);
        if (Math.Abs(scaling - _lastScaling) > 0.01)
        {
            _lastScaling = scaling;
            _webView.SetHidpiScale((float)scaling);
        }
    }

    private (uint Width, uint Height) ComputePixelSize(double scaling)
    {
        var (lw, lh) = _platform.GetLogicalSize();
        var w = (uint)Math.Max(1, (int)(lw * scaling));
        var h = (uint)Math.Max(1, (int)(lh * scaling));
        return (w, h);
    }

    // Navigation passthrough buffers until the web view exists.
    public void Navigate(string url)
    {
        if (_webView != null) _webView.Load(url);
        else _pendingUrl = url;
    }

    public void Navigate(Uri uri) => Navigate(uri.AbsoluteUri);
    public void Reload() => _webView?.Reload();
    public void GoBack(int steps = 1) => _webView?.GoBack(steps);
    public void GoForward(int steps = 1) => _webView?.GoForward(steps);

    private float _pendingZoom = 1.0f;

    /// <summary>
    /// Discrete page zoom factor (ex. 1.0 = 100%). Set before <see cref="Initialize"/> is buffered and
    /// applied once the web view exists.
    /// </summary>
    public float PageZoom
    {
        get => _webView?.PageZoom ?? _pendingZoom;
        set
        {
            _pendingZoom = value;
            if (_webView != null) _webView.PageZoom = value;
        }
    }

    /// <summary>Whether an IME composition is currently in progress.</summary>
    public bool ImeComposing => _imeComposing;

    /// <summary>The marked (pre-edit) text of the active composition.</summary>
    public string ImeMarkedText => _markedText;

    /// <summary>Start or update a composition with the given marked text; an empty string dismisses it.</summary>
    public void SetMarkedText(string text)
    {
        if (_webView == null) return;
        _markedText = text;

        if (!_imeComposing && text.Length > 0)
        {
            _imeComposing = true;
            _webView.SendImeComposition(CompositionState.Start, string.Empty);
        }

        if (!_imeComposing) return;

        _webView.SendImeComposition(CompositionState.Update, text);
    }

    /// <summary>Commit <paramref name="text"/>, ending any active composition.</summary>
    public void CommitComposition(string text)
    {
        if (_webView == null) return;
        _imeComposing = false;
        _markedText = string.Empty;
        _webView.SendImeComposition(CompositionState.End, text);
    }

    /// <summary>End the active composition, committing the current marked text.</summary>
    public void CancelComposition()
    {
        if (_webView == null) return;
        if (_imeComposing)
        {
            _imeComposing = false;
            _webView.SendImeComposition(CompositionState.End, _markedText);
        }
        _markedText = string.Empty;
    }

    /// <summary>Clear composition state without sending anything to the engine (e.g. on hide-embedder-control).</summary>
    public void ResetComposition()
    {
        _imeComposing = false;
        _markedText = string.Empty;
    }

    /// <summary>Send <paramref name="text"/> as a sequence of character key events (non-composing input).</summary>
    public void SendText(string text)
    {
        if (_webView == null) return;
        foreach (var ch in text)
        {
            _webView.SendKeyEvent(down: true, keyChar: ch, keyCode: null);
            _webView.SendKeyEvent(down: false, keyChar: ch, keyCode: null);
        }
    }

    public void Dispose()
    {
        _webView?.Dispose();
        _webView = null;
    }
}
