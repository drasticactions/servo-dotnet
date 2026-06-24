namespace Servo;

public sealed class InputMethodEventArgs(
    InputMethodType inputMethodType,
    string text,
    int? insertionPoint,
    bool multiline,
    bool allowVirtualKeyboard,
    int positionX,
    int positionY,
    int positionWidth,
    int positionHeight) : EventArgs
{
    public InputMethodType InputMethodType { get; } = inputMethodType;
    public string Text { get; } = text;
    public int? InsertionPoint { get; } = insertionPoint;
    public bool Multiline { get; } = multiline;
    public bool AllowVirtualKeyboard { get; } = allowVirtualKeyboard;
    public int PositionX { get; } = positionX;
    public int PositionY { get; } = positionY;
    public int PositionWidth { get; } = positionWidth;
    public int PositionHeight { get; } = positionHeight;
}
