using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_UsernameBox", typeof(TextBox))]
[TemplatePart("PART_PasswordBox", typeof(TextBox))]
[TemplatePart("PART_SignInButton", typeof(Button))]
[TemplatePart("PART_CancelButton", typeof(Button))]
[TemplatePart("PART_PromptText", typeof(TextBlock))]
public class AuthenticationOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> PromptTextProperty =
        AvaloniaProperty.Register<AuthenticationOverlay, string>(nameof(PromptText), "");

    private AuthenticationRequestEventArgs? _request;
    private Panel? _host;
    private TextBox? _usernameBox;
    private TextBox? _passwordBox;
    private bool _closed;

    public string PromptText
    {
        get => GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public void Initialize(Panel host, AuthenticationRequestEventArgs request)
    {
        _request = request;
        _host = host;

        string hostName;
        try { hostName = new Uri(request.Url).Host; }
        catch { hostName = request.Url; }

        PromptText = request.ForProxy
            ? "The proxy server requires authentication."
            : $"The server at {hostName} requires a username and password.";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        if (backdrop != null)
            backdrop.PointerPressed += OnBackdropPointerPressed;

        _usernameBox = e.NameScope.Find<TextBox>("PART_UsernameBox");
        _passwordBox = e.NameScope.Find<TextBox>("PART_PasswordBox");

        if (_passwordBox != null)
        {
            _passwordBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    Submit();
                    ke.Handled = true;
                }
            };
        }

        var signIn = e.NameScope.Find<Button>("PART_SignInButton");
        if (signIn != null)
            signIn.Click += (_, _) => Submit();

        var cancel = e.NameScope.Find<Button>("PART_CancelButton");
        if (cancel != null)
            cancel.Click += (_, _) => Close(() => _request?.Dismiss());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _usernameBox?.Focus();
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Close(() => _request?.Dismiss());
        e.Handled = true;
    }

    public void DismissIfOpen()
    {
        Close(() => _request?.Dismiss());
    }

    private void Submit()
    {
        Close(() => _request?.Authenticate(
            _usernameBox?.Text ?? "",
            _passwordBox?.Text ?? ""));
    }

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
    }
}
