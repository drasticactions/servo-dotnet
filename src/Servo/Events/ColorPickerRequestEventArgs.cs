namespace Servo;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

public sealed class ColorPickerRequestEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    internal ColorPickerRequestEventArgs(
        RgbColor? currentColor,
        int posX, int posY, int posW, int posH,
        nuint handle)
    {
        CurrentColor = currentColor;
        PositionX = posX;
        PositionY = posY;
        PositionWidth = posW;
        PositionHeight = posH;
        _handle = handle;
    }

    public RgbColor? CurrentColor { get; }
    public int PositionX { get; }
    public int PositionY { get; }
    public int PositionWidth { get; }
    public int PositionHeight { get; }

    public unsafe void Select(RgbColor color)
    {
        if (_responded) return;
        _responded = true;
        ServoNative.color_picker_select_and_submit(_handle, 1, color.Red, color.Green, color.Blue);
    }

    public unsafe void SelectNone()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.color_picker_select_and_submit(_handle, 0, 0, 0, 0);
    }

    public unsafe void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.color_picker_dismiss(_handle);
    }
}
