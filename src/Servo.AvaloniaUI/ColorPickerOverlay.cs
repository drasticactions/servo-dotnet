using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_RedSlider", typeof(Slider))]
[TemplatePart("PART_GreenSlider", typeof(Slider))]
[TemplatePart("PART_BlueSlider", typeof(Slider))]
[TemplatePart("PART_HexInput", typeof(TextBox))]
[TemplatePart("PART_Preview", typeof(Border))]
[TemplatePart("PART_OkButton", typeof(Button))]
[TemplatePart("PART_CancelButton", typeof(Button))]
public class ColorPickerOverlay : TemplatedControl
{
    public static readonly StyledProperty<double> RedValueProperty =
        AvaloniaProperty.Register<ColorPickerOverlay, double>(nameof(RedValue));

    public static readonly StyledProperty<double> GreenValueProperty =
        AvaloniaProperty.Register<ColorPickerOverlay, double>(nameof(GreenValue));

    public static readonly StyledProperty<double> BlueValueProperty =
        AvaloniaProperty.Register<ColorPickerOverlay, double>(nameof(BlueValue));

    public static readonly StyledProperty<IBrush?> PreviewBrushProperty =
        AvaloniaProperty.Register<ColorPickerOverlay, IBrush?>(nameof(PreviewBrush));

    public static readonly StyledProperty<string> HexTextProperty =
        AvaloniaProperty.Register<ColorPickerOverlay, string>(nameof(HexText), "#000000");

    private ColorPickerRequestEventArgs? _request;
    private Panel? _host;
    private TextBox? _hexInput;
    private bool _closed;
    private bool _updatingHex;

    public double RedValue
    {
        get => GetValue(RedValueProperty);
        set => SetValue(RedValueProperty, value);
    }

    public double GreenValue
    {
        get => GetValue(GreenValueProperty);
        set => SetValue(GreenValueProperty, value);
    }

    public double BlueValue
    {
        get => GetValue(BlueValueProperty);
        set => SetValue(BlueValueProperty, value);
    }

    public IBrush? PreviewBrush
    {
        get => GetValue(PreviewBrushProperty);
        set => SetValue(PreviewBrushProperty, value);
    }

    public string HexText
    {
        get => GetValue(HexTextProperty);
        set => SetValue(HexTextProperty, value);
    }

    public void Initialize(Panel host, ColorPickerRequestEventArgs request)
    {
        _request = request;
        _host = host;

        if (request.CurrentColor is { } color)
        {
            RedValue = color.Red;
            GreenValue = color.Green;
            BlueValue = color.Blue;
        }

        UpdatePreviewAndHex();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        if (backdrop != null)
            backdrop.PointerPressed += OnBackdropPointerPressed;

        _hexInput = e.NameScope.Find<TextBox>("PART_HexInput");
        if (_hexInput != null)
        {
            _hexInput.LostFocus += (_, _) => ParseHexInput();
            _hexInput.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    ParseHexInput();
                    ke.Handled = true;
                }
            };
        }

        var ok = e.NameScope.Find<Button>("PART_OkButton");
        if (ok != null)
            ok.Click += (_, _) => Submit();

        var cancel = e.NameScope.Find<Button>("PART_CancelButton");
        if (cancel != null)
            cancel.Click += (_, _) => Close(() => _request?.Dismiss());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RedValueProperty ||
            change.Property == GreenValueProperty ||
            change.Property == BlueValueProperty)
        {
            UpdatePreviewAndHex();
        }
    }

    private byte R => (byte)Math.Clamp(RedValue, 0, 255);
    private byte G => (byte)Math.Clamp(GreenValue, 0, 255);
    private byte B => (byte)Math.Clamp(BlueValue, 0, 255);

    private void UpdatePreviewAndHex()
    {
        var color = Color.FromRgb(R, G, B);
        if (PreviewBrush is SolidColorBrush existing)
            existing.Color = color;
        else
            PreviewBrush = new SolidColorBrush(color);
        if (!_updatingHex)
            HexText = $"#{R:X2}{G:X2}{B:X2}";
    }

    private void ParseHexInput()
    {
        var text = _hexInput?.Text?.Trim() ?? "";
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length == 6 &&
            byte.TryParse(text[..2], NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(text[2..4], NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(text[4..6], NumberStyles.HexNumber, null, out var b))
        {
            _updatingHex = true;
            RedValue = r;
            GreenValue = g;
            BlueValue = b;
            _updatingHex = false;
            UpdatePreviewAndHex();
        }
    }

    private void Submit()
    {
        Close(() => _request?.Select(new RgbColor(R, G, B)));
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

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
    }
}
