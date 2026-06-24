using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_OkButton", typeof(Button))]
[TemplatePart("PART_CancelButton", typeof(Button))]
public class ConfirmOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ConfirmOverlay, string>(nameof(Message), "");

    private ConfirmRequestEventArgs? _request;
    private Action? _onClosed;
    private Panel? _host;
    private Button? _okButton;
    private bool _closed;

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Initialize(Panel host, ConfirmRequestEventArgs request, Action? onClosed = null)
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
            _okButton.Click += (_, _) => Close(() => _request?.Confirm());

        var cancel = e.NameScope.Find<Button>("PART_CancelButton");
        if (cancel != null)
            cancel.Click += (_, _) => Close(() => _request?.Cancel());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _okButton?.Focus();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close(() => _request?.Cancel());
        e.Handled = true;
    }

    public void DismissIfOpen() => Close(() => _request?.Cancel());

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
        _onClosed?.Invoke();
    }
}
