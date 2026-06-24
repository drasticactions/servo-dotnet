using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_OkButton", typeof(Button))]
public class AlertOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<AlertOverlay, string>(nameof(Message), "");

    private AlertRequestEventArgs? _request;
    private Action? _onClosed;
    private Panel? _host;
    private Button? _okButton;
    private bool _closed;

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Initialize(Panel host, AlertRequestEventArgs request, Action? onClosed = null)
    {
        _request = request;
        _host = host;
        _onClosed = onClosed;
        Message = request.Message;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        if (backdrop != null)
            backdrop.PointerPressed += OnBackdropPressed;

        _okButton = e.NameScope.Find<Button>("PART_OkButton");
        if (_okButton != null)
            _okButton.Click += (_, _) => Close();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _okButton?.Focus();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    public void DismissIfOpen() => Close();

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        _request?.Dismiss();
        _onClosed?.Invoke();
    }
}
