using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Servo.Hosting;

namespace Servo.AvaloniaUI;

public class ServoWebViewControl : Control, IServoHostPlatform
{
    public static readonly StyledProperty<ServoEngine?> EngineProperty =
        AvaloniaProperty.Register<ServoWebViewControl, ServoEngine?>(nameof(Engine));

    public static readonly StyledProperty<Uri?> SourceProperty =
        AvaloniaProperty.Register<ServoWebViewControl, Uri?>(nameof(Source));

    public static readonly DirectProperty<ServoWebViewControl, string?> PageTitleProperty =
        AvaloniaProperty.RegisterDirect<ServoWebViewControl, string?>(nameof(PageTitle), o => o.PageTitle);

    public static readonly DirectProperty<ServoWebViewControl, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<ServoWebViewControl, bool>(nameof(IsLoading), o => o.IsLoading);

    public static readonly DirectProperty<ServoWebViewControl, bool> CanGoBackProperty =
        AvaloniaProperty.RegisterDirect<ServoWebViewControl, bool>(nameof(CanGoBack), o => o.CanGoBack);

    public static readonly DirectProperty<ServoWebViewControl, bool> CanGoForwardProperty =
        AvaloniaProperty.RegisterDirect<ServoWebViewControl, bool>(nameof(CanGoForward), o => o.CanGoForward);

    public ServoEngine? Engine
    {
        get => GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? PageTitle
    {
        get => _pageTitle;
        private set => SetAndRaise(PageTitleProperty, ref _pageTitle, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetAndRaise(CanGoBackProperty, ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => SetAndRaise(CanGoForwardProperty, ref _canGoForward, value);
    }

    /// <summary>
    /// Discrete page zoom factor (ex. 1.0 = 100%). Setting this changes the rendered page zoom; a value
    /// set before the control is attached is buffered and applied once the web view is created.
    /// </summary>
    public float PageZoom
    {
        get => _host?.PageZoom ?? _pendingZoom;
        set
        {
            _pendingZoom = value;
            if (_host != null) _host.PageZoom = value;
        }
    }

    public event EventHandler<UrlChangedEventArgs>? Navigated;
    public event EventHandler<TitleChangedEventArgs>? TitleChanged;
    public event EventHandler<LoadStatusChangedEventArgs>? LoadStatusChanged;
    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;
    public event EventHandler<ConsoleMessageEventArgs>? ConsoleMessage;
    public event EventHandler<CrashedEventArgs>? Crashed;
    public event EventHandler<AlertRequestEventArgs>? AlertRequested;
    public event EventHandler<ConfirmRequestEventArgs>? ConfirmRequested;
    public event EventHandler<PromptRequestEventArgs>? PromptRequested;
    public event EventHandler<SelectElementRequestEventArgs>? SelectElementRequested;
    public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;
    public event EventHandler<CreateNewWebViewRequestEventArgs>? CreateNewWebViewRequested;
    public event EventHandler<AuthenticationRequestEventArgs>? AuthenticationRequested;
    public event EventHandler? HideEmbedderControlRequested;
    public event EventHandler<WebResourceLoadEventArgs>? WebResourceLoadRequested;
    public event EventHandler<StatusTextChangedEventArgs>? StatusTextChanged;
    public event EventHandler? TraversalCompleted;
    public event EventHandler<MoveToRequestEventArgs>? MoveToRequested;
    public event EventHandler<ResizeToRequestEventArgs>? ResizeToRequested;
    public event EventHandler<ProtocolHandlerRequestEventArgs>? ProtocolHandlerRequested;
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;
    public event EventHandler<NotificationEventArgs>? NotificationRequested;
    public event EventHandler<BluetoothDeviceSelectionEventArgs>? BluetoothDeviceSelectionRequested;
    public event EventHandler? FaviconChanged;
    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
    public event EventHandler<FilePickerRequestEventArgs>? FilePickerRequested;
    public event EventHandler<ColorPickerRequestEventArgs>? ColorPickerRequested;
    public event EventHandler<InputMethodEventArgs>? InputMethodRequested;

    private string? _pageTitle;
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;

    private ServoWebViewHost? _host;
    private ServoWebView? _webView;
    private RenderingContext? _renderingContext;
    private ServoSurface? _surface;
    private Panel? _contentHost;
    private double _cachedScaling = 1.0;
    private float _pendingZoom = 1.0f;
    private TopLevel? _topLevel;
    private AlertOverlay? _activeAlertOverlay;
    private ConfirmOverlay? _activeConfirmOverlay;
    private PromptOverlay? _activePromptOverlay;
    private SelectElementOverlay? _activeSelectOverlay;
    private AuthenticationOverlay? _activeAuthOverlay;
    private ProtocolHandlerOverlay? _activeProtocolHandlerOverlay;
    private PermissionOverlay? _activePermissionOverlay;
    private BluetoothDeviceOverlay? _activeBluetoothOverlay;
    private ContextMenu? _activeContextMenu;
    private ColorPickerOverlay? _activeColorPickerOverlay;
    private ServoTextInputMethodClient? _imeClient;

    private bool HasModalOverlay =>
        _activeAlertOverlay != null ||
        _activeConfirmOverlay != null ||
        _activePromptOverlay != null ||
        _activeSelectOverlay != null ||
        _activeAuthOverlay != null ||
        _activeProtocolHandlerOverlay != null ||
        _activePermissionOverlay != null ||
        _activeBluetoothOverlay != null ||
        _activeColorPickerOverlay != null ||
        _activeContextMenu != null;

    static ServoWebViewControl()
    {
        TextInputMethodClientRequestedEvent.AddClassHandler<ServoWebViewControl>(
            (control, e) =>
            {
                control._imeClient ??= new ServoTextInputMethodClient(control);
                e.Client = control._imeClient;
            });
    }

    public ServoWebViewControl()
    {
        Focusable = true;
    }

    public ServoRenderingBackend RenderingBackend { get; set; } = ServoRenderingBackend.Hardware;

    public nuint? PendingCreateNewWebViewRequest { get; set; }

    public ServoWebView? WebView => _webView;

    /// <summary>The shared engine host; <c>null</c> until the control is attached and initialized.</summary>
    public ServoWebViewHost? Host => _host;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _surface = new ServoSurface();
        _contentHost = new Panel();
        _contentHost.Children.Add(_surface);

        ((ISetLogicalParent)_contentHost).SetParent(this);
        VisualChildren.Add(_contentHost);
        LogicalChildren.Add(_contentHost);

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
            _topLevel.ScalingChanged += OnScalingChanged;

        ActualThemeVariantChanged += OnThemeVariantChanged;
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);

        Dispatcher.UIThread.Post(InitializeServo, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }

        ActualThemeVariantChanged -= OnThemeVariantChanged;
        RemoveHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);

        Cleanup();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _contentHost?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _contentHost?.Measure(availableSize);
        return availableSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty && _host != null)
        {
            var uri = change.GetNewValue<Uri?>();
            if (uri != null) _host.Navigate(uri.AbsoluteUri);
        }
        else if (change.Property == BoundsProperty)
        {
            ResizeServo();
        }
    }

    public void Navigate(string url) => _host?.Navigate(url);
    public void Navigate(Uri uri) => Navigate(uri.AbsoluteUri);
    public void Reload() => _host?.Reload();
    public void GoBack(int steps = 1) => _host?.GoBack(steps);
    public void GoForward(int steps = 1) => _host?.GoForward(steps);
    public Task<string> EvaluateJavaScriptAsync(string script) =>
        _webView?.EvaluateJavaScriptAsync(script) ?? Task.FromResult("undefined");

    public FaviconData? GetFavicon() => _webView?.GetFavicon();

    public void NotifyThemeChange(ServoTheme theme) => _webView?.NotifyThemeChange(theme);

    public void NotifyMediaSessionAction(MediaSessionAction action) =>
        _webView?.NotifyMediaSessionAction(action);

    public void AdjustPinchZoom(float delta, float centerX, float centerY) =>
        _webView?.AdjustPinchZoom(delta, centerX, centerY);

    private void InitializeServo()
    {
        if (_host != null) return; // already initialized

        var engine = Engine ?? ServoLocator.Engine;
        _cachedScaling = GetScaling();

        _host = new ServoWebViewHost(this);
        _host.Navigated += OnWebViewUrlChanged;
        _host.TitleChanged += OnWebViewTitleChanged;
        _host.LoadStatusChanged += OnWebViewLoadStatusChanged;
        _host.CursorChanged += OnWebViewCursorChanged;
        _host.FaviconChanged += OnWebViewFaviconChanged;
        _host.HistoryChanged += OnWebViewHistoryChanged;
        _host.Crashed += OnWebViewCrashed;
        _host.ConsoleMessage += OnWebViewConsoleMessage;
        _host.StatusTextChanged += OnWebViewStatusTextChanged;
        _host.TraversalCompleted += OnWebViewTraversalCompleted;
        _host.MoveToRequested += OnWebViewMoveToRequested;
        _host.ResizeToRequested += OnWebViewResizeToRequested;
        _host.NavigationRequested += OnWebViewNavigationRequested;
        _host.WebResourceLoadRequested += OnWebViewWebResourceLoadRequested;
        _host.AlertRequested += OnWebViewAlertRequested;
        _host.ConfirmRequested += OnWebViewConfirmRequested;
        _host.PromptRequested += OnWebViewPromptRequested;
        _host.SelectElementRequested += OnWebViewSelectElementRequested;
        _host.ContextMenuRequested += OnWebViewContextMenuRequested;
        _host.CreateNewWebViewRequested += OnWebViewCreateNewWebViewRequested;
        _host.AuthenticationRequested += OnWebViewAuthenticationRequested;
        _host.HideEmbedderControlRequested += OnWebViewHideEmbedderControlRequested;
        _host.ProtocolHandlerRequested += OnWebViewProtocolHandlerRequested;
        _host.PermissionRequested += OnWebViewPermissionRequested;
        _host.NotificationRequested += OnWebViewNotificationRequested;
        _host.BluetoothDeviceSelectionRequested += OnWebViewBluetoothDeviceSelectionRequested;
        _host.FilePickerRequested += OnWebViewFilePickerRequested;
        _host.ColorPickerRequested += OnWebViewColorPickerRequested;
        _host.InputMethodRequested += OnWebViewInputMethodRequested;

        // Seed any zoom set before attach; the host buffers it and applies it during Initialize.
        _host.PageZoom = _pendingZoom;

        if (PendingCreateNewWebViewRequest is { } requestHandle)
        {
            PendingCreateNewWebViewRequest = null;
            _host.InitializeFromCreateRequest(requestHandle);
        }
        else
        {
            _host.Initialize(engine, Source?.AbsoluteUri);
        }

        _webView = _host.WebView;
        _surface!.SetRenderingContext(_renderingContext!);
    }

    private void Cleanup()
    {
        // Disposing the host tears down the web view and all native callbacks; because the host is
        // the event source and is being dropped, the control's handlers don't need unsubscribing.
        _host?.Dispose();
        _host = null;
        _webView = null;

        if (_contentHost != null)
        {
            VisualChildren.Remove(_contentHost);
            LogicalChildren.Remove(_contentHost);
            _contentHost = null;
        }
        _surface = null;
        _renderingContext?.Dispose();
        _renderingContext = null;
    }


    void IServoHostPlatform.Post(Action action) => Dispatcher.UIThread.Post(action);

    void IServoHostPlatform.PostRender(Action action) =>
        Dispatcher.UIThread.Post(action, DispatcherPriority.Render);

    RenderingContext IServoHostPlatform.CreateRenderingContext(uint pixelWidth, uint pixelHeight)
    {
        _renderingContext = RenderingBackend == ServoRenderingBackend.Hardware
            ? new HardwareRenderingContext(pixelWidth, pixelHeight)
            : new SoftwareRenderingContext(pixelWidth, pixelHeight);
        return _renderingContext;
    }

    void IServoHostPlatform.PresentFrame() => _surface?.MarkFrameReady();

    double IServoHostPlatform.GetScaling() => GetScaling();

    (double Width, double Height) IServoHostPlatform.GetLogicalSize() => (Bounds.Width, Bounds.Height);

    ServoTheme IServoHostPlatform.Theme => GetServoTheme();

    private void OnScalingChanged(object? sender, EventArgs e) => ResizeServo();

    private void OnThemeVariantChanged(object? sender, EventArgs e) =>
        _webView?.NotifyThemeChange(GetServoTheme());

    private ServoTheme GetServoTheme() =>
        ActualThemeVariant == ThemeVariant.Dark ? ServoTheme.Dark : ServoTheme.Light;

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_webView == null || HasModalOverlay) return;
        var pos = e.GetPosition(this);
        var s = _cachedScaling;
        var delta = 1.0f + (float)e.Delta.X;
        _webView.AdjustPinchZoom(delta, (float)(pos.X * s), (float)(pos.Y * s));
        (Engine ?? ServoLocator.Engine).SpinEventLoop();
    }

    private void ResizeServo()
    {
        if (_host == null) return;
        _cachedScaling = GetScaling();
        _host.Resize();
    }

    private double GetScaling() =>
        (this.GetPresentationSource()?.RenderScaling) ?? 1.0;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_webView == null || HasModalOverlay) return;
        Focus();
        var pos = e.GetPosition(this);
        var s = _cachedScaling;
        var props = e.GetCurrentPoint(this).Properties;
        var button = ServoMouseButton.Left;
        if (props.IsMiddleButtonPressed) button = ServoMouseButton.Middle;
        else if (props.IsRightButtonPressed) button = ServoMouseButton.Right;
        else if (props.IsXButton1Pressed) button = ServoMouseButton.Back;
        else if (props.IsXButton2Pressed) button = ServoMouseButton.Forward;
        _webView.SendMouseButton(MouseButtonAction.Down, button, (float)(pos.X * s), (float)(pos.Y * s));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_webView == null || HasModalOverlay) return;
        var pos = e.GetPosition(this);
        var s = _cachedScaling;
        var button = AvaloniaKeyMapping.ToServoButton(e.InitialPressMouseButton);
        _webView.SendMouseButton(MouseButtonAction.Up, button, (float)(pos.X * s), (float)(pos.Y * s));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_webView == null || HasModalOverlay) return;
        var pos = e.GetPosition(this);
        var s = _cachedScaling;
        _webView.SendMouseMove((float)(pos.X * s), (float)(pos.Y * s));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _webView?.SendMouseLeftViewport();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_webView == null || HasModalOverlay) return;
        var pos = e.GetPosition(this);
        var s = _cachedScaling;
        _webView.SendWheel(e.Delta.X * 40.0, e.Delta.Y * 40.0, WheelMode.DeltaPixel,
            (float)(pos.X * s), (float)(pos.Y * s));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_webView == null || HasModalOverlay) return;

        if (e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control))
        {
            var action = e.Key switch
            {
                Key.C => (EditingAction?)EditingAction.Copy,
                Key.X => (EditingAction?)EditingAction.Cut,
                Key.V => (EditingAction?)EditingAction.Paste,
                _ => null,
            };
            if (action.HasValue)
            {
                _webView.SendEditingAction(action.Value);
                e.Handled = true;
                return;
            }
        }

        _webView.SendKeyEvent(down: true, keyChar: 0,
            keyCode: AvaloniaKeyMapping.ToServoCode(e.Key),
            modifiers: AvaloniaKeyMapping.ToServoModifiers(e.KeyModifiers));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (HasModalOverlay) return;
        _webView?.SendKeyEvent(down: false, keyChar: 0,
            keyCode: AvaloniaKeyMapping.ToServoCode(e.Key),
            modifiers: AvaloniaKeyMapping.ToServoModifiers(e.KeyModifiers));
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_host == null || HasModalOverlay || string.IsNullOrEmpty(e.Text)) return;

        if (_host.ImeComposing)
            _imeClient?.NotifyCompositionEnd(e.Text);
        else
            _host.SendText(e.Text);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _webView?.Focus();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _host?.CancelComposition();
        _webView?.Blur();
    }

    // ----- Host event handlers (already marshalled onto the UI thread by the host) -----

    private void OnWebViewLoadStatusChanged(object? sender, LoadStatusChangedEventArgs e)
    {
        IsLoading = e.Status != LoadStatus.Complete;
        LoadStatusChanged?.Invoke(this, e);
    }

    private void OnWebViewUrlChanged(object? sender, UrlChangedEventArgs e) =>
        Navigated?.Invoke(this, e);

    private void OnWebViewTitleChanged(object? sender, TitleChangedEventArgs e)
    {
        PageTitle = e.Title;
        TitleChanged?.Invoke(this, e);
    }

    private void OnWebViewCursorChanged(object? sender, CursorChangedEventArgs e) =>
        Cursor = new Cursor(AvaloniaKeyMapping.ToAvaloniaCursor(e.Cursor));

    private void OnWebViewFaviconChanged(object? sender, EventArgs e) =>
        FaviconChanged?.Invoke(this, EventArgs.Empty);

    private void OnWebViewHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        CanGoBack = _webView?.CanGoBack ?? false;
        CanGoForward = _webView?.CanGoForward ?? false;
        HistoryChanged?.Invoke(this, e);
    }

    private void OnWebViewCrashed(object? sender, CrashedEventArgs e) =>
        Crashed?.Invoke(this, e);

    private void OnWebViewConsoleMessage(object? sender, ConsoleMessageEventArgs e) =>
        ConsoleMessage?.Invoke(this, e);

    private void OnWebViewNavigationRequested(object? sender, NavigationRequestEventArgs e)
    {
        if (NavigationRequested != null) NavigationRequested.Invoke(this, e);
        else e.Allow();
    }

    private void OnWebViewAlertRequested(object? sender, AlertRequestEventArgs e)
    {
        if (_contentHost == null) { e.Dismiss(); return; }
        if (AlertRequested != null)
        {
            AlertRequested.Invoke(this, e);
            return;
        }
        var overlay = new AlertOverlay();
        overlay.Initialize(_contentHost, e, () => _activeAlertOverlay = null);
        _activeAlertOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewConfirmRequested(object? sender, ConfirmRequestEventArgs e)
    {
        if (_contentHost == null) { e.Cancel(); return; }
        if (ConfirmRequested != null)
        {
            ConfirmRequested.Invoke(this, e);
            return;
        }
        var overlay = new ConfirmOverlay();
        overlay.Initialize(_contentHost, e, () => _activeConfirmOverlay = null);
        _activeConfirmOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewPromptRequested(object? sender, PromptRequestEventArgs e)
    {
        if (_contentHost == null) { e.Cancel(); return; }
        if (PromptRequested != null)
        {
            PromptRequested.Invoke(this, e);
            return;
        }
        var overlay = new PromptOverlay();
        overlay.Initialize(_contentHost, e, () => _activePromptOverlay = null);
        _activePromptOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewSelectElementRequested(object? sender, SelectElementRequestEventArgs e)
    {
        if (_contentHost == null) { e.Dismiss(); return; }
        if (SelectElementRequested != null)
        {
            SelectElementRequested.Invoke(this, e);
            return;
        }
        var overlay = new SelectElementOverlay();
        overlay.Initialize(_contentHost, e);
        _activeSelectOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewContextMenuRequested(object? sender, ContextMenuRequestEventArgs e)
    {
        if (ContextMenuRequested != null)
        {
            ContextMenuRequested.Invoke(this, e);
            return;
        }
        ShowDefaultContextMenu(e);
    }

    private void OnWebViewCreateNewWebViewRequested(object? sender, CreateNewWebViewRequestEventArgs e)
    {
        var handler = CreateNewWebViewRequested;
        if (handler != null)
        {
            handler.Invoke(this, e);
            if (!e.IsHandled)
                e.Dismiss();
        }
        else
        {
            e.Dismiss();
        }
    }

    private void OnWebViewAuthenticationRequested(object? sender, AuthenticationRequestEventArgs e)
    {
        if (_contentHost == null) { e.Dismiss(); return; }
        if (AuthenticationRequested != null)
        {
            AuthenticationRequested.Invoke(this, e);
            return;
        }
        var overlay = new AuthenticationOverlay();
        overlay.Initialize(_contentHost, e);
        _activeAuthOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewHideEmbedderControlRequested(object? sender, EventArgs e)
    {
        DismissActiveEmbedderControls();
        HideEmbedderControlRequested?.Invoke(this, e);
    }

    private void OnWebViewWebResourceLoadRequested(object? sender, WebResourceLoadEventArgs e)
    {
        if (WebResourceLoadRequested != null)
            WebResourceLoadRequested.Invoke(this, e);
        else
            e.Allow();
    }

    private void OnWebViewStatusTextChanged(object? sender, StatusTextChangedEventArgs e) =>
        StatusTextChanged?.Invoke(this, e);

    private void OnWebViewTraversalCompleted(object? sender, EventArgs e) =>
        TraversalCompleted?.Invoke(this, e);

    private void OnWebViewMoveToRequested(object? sender, MoveToRequestEventArgs e) =>
        MoveToRequested?.Invoke(this, e);

    private void OnWebViewResizeToRequested(object? sender, ResizeToRequestEventArgs e) =>
        ResizeToRequested?.Invoke(this, e);

    private void OnWebViewProtocolHandlerRequested(object? sender, ProtocolHandlerRequestEventArgs e)
    {
        if (_contentHost == null) { e.Deny(); return; }
        if (ProtocolHandlerRequested != null)
        {
            ProtocolHandlerRequested.Invoke(this, e);
            return;
        }
        var overlay = new ProtocolHandlerOverlay();
        overlay.Initialize(_contentHost, e);
        _activeProtocolHandlerOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewPermissionRequested(object? sender, PermissionRequestEventArgs e)
    {
        if (_contentHost == null) { e.Deny(); return; }
        if (PermissionRequested != null)
        {
            PermissionRequested.Invoke(this, e);
            return;
        }
        var overlay = new PermissionOverlay();
        overlay.Initialize(_contentHost, e, () => _activePermissionOverlay = null);
        _activePermissionOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewNotificationRequested(object? sender, NotificationEventArgs e)
    {
        if (_contentHost == null) return;
        if (NotificationRequested != null)
        {
            NotificationRequested.Invoke(this, e);
            return;
        }
        var overlay = new NotificationOverlay();
        overlay.Initialize(_contentHost, e);
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewBluetoothDeviceSelectionRequested(object? sender, BluetoothDeviceSelectionEventArgs e)
    {
        if (_contentHost == null) { e.Cancel(); return; }
        if (BluetoothDeviceSelectionRequested != null)
        {
            BluetoothDeviceSelectionRequested.Invoke(this, e);
            return;
        }
        var overlay = new BluetoothDeviceOverlay();
        overlay.Initialize(_contentHost, e);
        _activeBluetoothOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewFilePickerRequested(object? sender, FilePickerRequestEventArgs e)
    {
        if (FilePickerRequested != null)
        {
            FilePickerRequested.Invoke(this, e);
            return;
        }
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            _ = FilePickerHandler.HandleRequest(topLevel, e);
        else
            e.Dismiss();
    }

    private void OnWebViewColorPickerRequested(object? sender, ColorPickerRequestEventArgs e)
    {
        if (_contentHost == null) { e.Dismiss(); return; }
        if (ColorPickerRequested != null)
        {
            ColorPickerRequested.Invoke(this, e);
            return;
        }
        var overlay = new ColorPickerOverlay();
        overlay.Initialize(_contentHost, e);
        _activeColorPickerOverlay = overlay;
        _contentHost.Children.Add(overlay);
    }

    private void OnWebViewInputMethodRequested(object? sender, InputMethodEventArgs e)
    {
        _imeClient?.UpdateCursorRect(
            e.PositionX,
            e.PositionY,
            e.PositionWidth,
            e.PositionHeight);
        InputMethodRequested?.Invoke(this, e);
    }

    private void DismissActiveEmbedderControls()
    {
        if (_activeAlertOverlay != null)
        {
            _activeAlertOverlay.DismissIfOpen();
            _activeAlertOverlay = null;
        }

        if (_activeConfirmOverlay != null)
        {
            _activeConfirmOverlay.DismissIfOpen();
            _activeConfirmOverlay = null;
        }

        if (_activePromptOverlay != null)
        {
            _activePromptOverlay.DismissIfOpen();
            _activePromptOverlay = null;
        }

        if (_activeSelectOverlay != null)
        {
            _activeSelectOverlay.DismissIfOpen();
            _activeSelectOverlay = null;
        }

        if (_activeAuthOverlay != null)
        {
            _activeAuthOverlay.DismissIfOpen();
            _activeAuthOverlay = null;
        }

        if (_activeProtocolHandlerOverlay != null)
        {
            _activeProtocolHandlerOverlay.DismissIfOpen();
            _activeProtocolHandlerOverlay = null;
        }

        if (_activePermissionOverlay != null)
        {
            _activePermissionOverlay.DismissIfOpen();
            _activePermissionOverlay = null;
        }

        if (_activeBluetoothOverlay != null)
        {
            _activeBluetoothOverlay.DismissIfOpen();
            _activeBluetoothOverlay = null;
        }

        if (_activeColorPickerOverlay != null)
        {
            _activeColorPickerOverlay.DismissIfOpen();
            _activeColorPickerOverlay = null;
        }

        if (_activeContextMenu != null)
        {
            _activeContextMenu.Close();
            _activeContextMenu = null;
        }
    }

    private void ShowDefaultContextMenu(ContextMenuRequestEventArgs e)
    {
        var menu = new ContextMenu();

        foreach (var item in e.Items)
        {
            var menuItem = new MenuItem
            {
                Header = item.Label,
                IsEnabled = item.Enabled,
            };
            var action = item.Action;
            menuItem.Click += (_, _) => e.Select(action);
            menu.Items.Add(menuItem);
        }

        menu.Closed += (_, _) =>
        {
            _activeContextMenu = null;
            e.Dismiss();
        };

        _activeContextMenu = menu;
        menu.PlacementTarget = this;
        menu.Open(this);
    }
}
