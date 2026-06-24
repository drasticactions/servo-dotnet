using Avalonia.Input;

namespace Servo.AvaloniaUI;

internal static class AvaloniaKeyMapping
{
    public static string? ToServoCode(Key key) => key switch
    {
        Key.A => "KeyA", Key.B => "KeyB", Key.C => "KeyC", Key.D => "KeyD",
        Key.E => "KeyE", Key.F => "KeyF", Key.G => "KeyG", Key.H => "KeyH",
        Key.I => "KeyI", Key.J => "KeyJ", Key.K => "KeyK", Key.L => "KeyL",
        Key.M => "KeyM", Key.N => "KeyN", Key.O => "KeyO", Key.P => "KeyP",
        Key.Q => "KeyQ", Key.R => "KeyR", Key.S => "KeyS", Key.T => "KeyT",
        Key.U => "KeyU", Key.V => "KeyV", Key.W => "KeyW", Key.X => "KeyX",
        Key.Y => "KeyY", Key.Z => "KeyZ",
        Key.D0 => "Digit0", Key.D1 => "Digit1", Key.D2 => "Digit2",
        Key.D3 => "Digit3", Key.D4 => "Digit4", Key.D5 => "Digit5",
        Key.D6 => "Digit6", Key.D7 => "Digit7", Key.D8 => "Digit8", Key.D9 => "Digit9",
        Key.Return => "Enter", Key.Space => "Space", Key.Tab => "Tab",
        Key.Back => "Backspace", Key.Delete => "Delete", Key.Escape => "Escape",
        Key.Left => "ArrowLeft", Key.Right => "ArrowRight",
        Key.Up => "ArrowUp", Key.Down => "ArrowDown",
        Key.Home => "Home", Key.End => "End",
        Key.PageUp => "PageUp", Key.PageDown => "PageDown",
        Key.LeftShift or Key.RightShift => "ShiftLeft",
        Key.LeftCtrl or Key.RightCtrl => "ControlLeft",
        Key.LeftAlt or Key.RightAlt => "AltLeft",
        Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
        Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
        Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
        Key.OemMinus => "Minus", Key.OemPlus => "Equal",
        Key.OemOpenBrackets => "BracketLeft", Key.OemCloseBrackets => "BracketRight",
        Key.OemBackslash or Key.OemPipe => "Backslash",
        Key.OemSemicolon => "Semicolon", Key.OemQuotes => "Quote",
        Key.OemComma => "Comma", Key.OemPeriod => "Period",
        Key.Oem2 => "Slash", Key.OemTilde => "Backquote",
        _ => null,
    };

    public static KeyModifiers ToServoModifiers(global::Avalonia.Input.KeyModifiers mods)
    {
        var result = KeyModifiers.None;
        if (mods.HasFlag(global::Avalonia.Input.KeyModifiers.Control)) result |= KeyModifiers.Control;
        if (mods.HasFlag(global::Avalonia.Input.KeyModifiers.Alt)) result |= KeyModifiers.Alt;
        if (mods.HasFlag(global::Avalonia.Input.KeyModifiers.Meta)) result |= KeyModifiers.Meta;
        return result;
    }

    public static ServoMouseButton ToServoButton(global::Avalonia.Input.MouseButton button) => button switch
    {
        global::Avalonia.Input.MouseButton.Middle => ServoMouseButton.Middle,
        global::Avalonia.Input.MouseButton.Right => ServoMouseButton.Right,
        global::Avalonia.Input.MouseButton.XButton1 => ServoMouseButton.Back,
        global::Avalonia.Input.MouseButton.XButton2 => ServoMouseButton.Forward,
        _ => ServoMouseButton.Left,
    };

    public static StandardCursorType ToAvaloniaCursor(ServoCursor cursor) => cursor switch
    {
        ServoCursor.Pointer => StandardCursorType.Hand,
        ServoCursor.Text => StandardCursorType.Ibeam,
        ServoCursor.Crosshair => StandardCursorType.Cross,
        ServoCursor.Move => StandardCursorType.SizeAll,
        ServoCursor.Help => StandardCursorType.Help,
        ServoCursor.NotAllowed => StandardCursorType.No,
        ServoCursor.Progress or ServoCursor.Wait => StandardCursorType.Wait,
        ServoCursor.NResize or ServoCursor.SResize or ServoCursor.NsResize => StandardCursorType.SizeNorthSouth,
        ServoCursor.EResize or ServoCursor.WResize or ServoCursor.EwResize => StandardCursorType.SizeWestEast,
        ServoCursor.NeResize or ServoCursor.SwResize or ServoCursor.NeswResize => StandardCursorType.BottomLeftCorner,
        ServoCursor.NwResize or ServoCursor.SeResize or ServoCursor.NwseResize => StandardCursorType.BottomRightCorner,
        ServoCursor.None => StandardCursorType.None,
        _ => StandardCursorType.Arrow,
    };
}
