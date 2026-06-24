using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_AllowButton", typeof(Button))]
[TemplatePart("PART_DenyButton", typeof(Button))]
public class ProtocolHandlerOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> PromptTextProperty =
        AvaloniaProperty.Register<ProtocolHandlerOverlay, string>(nameof(PromptText), "");

    private ProtocolHandlerRequestEventArgs? _request;
    private Panel? _host;
    private bool _closed;

    public string PromptText
    {
        get => GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public void Initialize(Panel host, ProtocolHandlerRequestEventArgs request)
    {
        _request = request;
        _host = host;

        var action = request.Action == ProtocolHandlerAction.Register ? "register" : "unregister";
        PromptText = $"This page wants to {action} a handler for \"{request.Scheme}:\" links.";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        if (backdrop != null)
            backdrop.PointerPressed += OnBackdropPressed;

        var allow = e.NameScope.Find<Button>("PART_AllowButton");
        if (allow != null)
            allow.Click += (_, _) => Close(() => _request?.Allow());

        var deny = e.NameScope.Find<Button>("PART_DenyButton");
        if (deny != null)
            deny.Click += (_, _) => Close(() => _request?.Deny());
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close(() => _request?.Deny());
        e.Handled = true;
    }

    public void DismissIfOpen()
    {
        Close(() => _request?.Deny());
    }

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
    }
}
