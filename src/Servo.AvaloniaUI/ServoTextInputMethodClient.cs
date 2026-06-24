using Avalonia;
using Avalonia.Input.TextInput;

namespace Servo.AvaloniaUI;

internal class ServoTextInputMethodClient : TextInputMethodClient
{
    private readonly ServoWebViewControl _control;
    private TextSelection _selection;
    private Rect _cursorRect = new(0, 0, 1, 16);

    public ServoTextInputMethodClient(ServoWebViewControl control)
    {
        _control = control;
    }

    public override Visual TextViewVisual => _control;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => false;

    public override string SurroundingText => "";

    public override Rect CursorRectangle => _cursorRect;

    public override TextSelection Selection
    {
        get => _selection;
        set => _selection = value;
    }

    public void UpdateCursorRect(double x, double y, double width, double height)
    {
        _cursorRect = new Rect(x, y, Math.Max(1, width), Math.Max(1, height));
        RaiseCursorRectangleChanged();
    }

    public override void SetPreeditText(string? preeditText) => UpdatePreedit(preeditText);

    public override void SetPreeditText(string? preeditText, int? cursorPos) => UpdatePreedit(preeditText);

    // The composition state machine lives in the shared ServoWebViewHost; this client just drives it.
    private void UpdatePreedit(string? preeditText) => _control.Host?.SetMarkedText(preeditText ?? "");

    public void NotifyCompositionEnd(string committedText) => _control.Host?.CommitComposition(committedText);
}
