using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_InputBox", typeof(TextBox))]
[TemplatePart("PART_OkButton", typeof(Button))]
[TemplatePart("PART_CancelButton", typeof(Button))]
public class PromptOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<PromptOverlay, string>(nameof(Message), "");

    private PromptRequestEventArgs? _request;
    private Action? _onClosed;
    private Panel? _host;
    private TextBox? _inputBox;
    private bool _closed;

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Initialize(Panel host, PromptRequestEventArgs request, Action? onClosed = null)
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

        _inputBox = e.NameScope.Find<TextBox>("PART_InputBox");
        if (_inputBox != null)
        {
            _inputBox.Text = _request?.DefaultValue ?? "";
            _inputBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    Submit();
                    ke.Handled = true;
                }
            };
        }

        var ok = e.NameScope.Find<Button>("PART_OkButton");
        if (ok != null)
            ok.Click += (_, _) => Submit();

        var cancel = e.NameScope.Find<Button>("PART_CancelButton");
        if (cancel != null)
            cancel.Click += (_, _) => Close(() => _request?.Cancel());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputBox?.Focus();
        _inputBox?.SelectAll();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close(() => _request?.Cancel());
        e.Handled = true;
    }

    public void DismissIfOpen() => Close(() => _request?.Cancel());

    private void Submit() => Close(() => _request?.Respond(_inputBox?.Text ?? ""));

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
        _onClosed?.Invoke();
    }
}
