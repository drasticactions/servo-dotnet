using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_ListBox", typeof(ListBox))]
[TemplatePart("PART_CancelButton", typeof(Button))]
public class BluetoothDeviceOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> PromptTextProperty =
        AvaloniaProperty.Register<BluetoothDeviceOverlay, string>(nameof(PromptText), "");

    private BluetoothDeviceSelectionEventArgs? _request;
    private Panel? _host;
    private ListBox? _listBox;
    private bool _closed;

    public string PromptText
    {
        get => GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public void Initialize(Panel host, BluetoothDeviceSelectionEventArgs request)
    {
        _request = request;
        _host = host;
        PromptText = $"A page wants to connect to a Bluetooth device. {request.Devices.Count} device(s) found.";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        if (backdrop != null)
            backdrop.PointerPressed += OnBackdropPressed;

        _listBox = e.NameScope.Find<ListBox>("PART_ListBox");
        if (_listBox != null && _request != null)
            BuildItems();

        var cancel = e.NameScope.Find<Button>("PART_CancelButton");
        if (cancel != null)
            cancel.Click += (_, _) => Close(() => _request?.Cancel());
    }

    private void BuildItems()
    {
        if (_listBox == null || _request == null) return;

        for (int i = 0; i < _request.Devices.Count; i++)
        {
            var device = _request.Devices[i];
            var item = new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = string.IsNullOrEmpty(device.Name) ? device.Address : $"{device.Name} ({device.Address})",
                    Margin = new Thickness(4, 2),
                },
                Tag = i,
            };
            item.PointerReleased += OnItemPointerReleased;
            _listBox.Items.Add(item);
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is ListBoxItem { Tag: int index } && _request != null)
            Close(() => _request.PickDevice(index));
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close(() => _request?.Cancel());
        e.Handled = true;
    }

    public void DismissIfOpen()
    {
        Close(() => _request?.Cancel());
    }

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
    }
}
