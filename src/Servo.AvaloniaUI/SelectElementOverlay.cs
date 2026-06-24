using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_Backdrop", typeof(Panel))]
[TemplatePart("PART_Dropdown", typeof(Border))]
[TemplatePart("PART_ListBox", typeof(ListBox))]
public class SelectElementOverlay : TemplatedControl
{
    public static readonly StyledProperty<double> DropdownLeftProperty =
        AvaloniaProperty.Register<SelectElementOverlay, double>(nameof(DropdownLeft));

    public static readonly StyledProperty<double> DropdownTopProperty =
        AvaloniaProperty.Register<SelectElementOverlay, double>(nameof(DropdownTop));

    private SelectElementRequestEventArgs? _request;
    private Panel? _host;
    private ListBox? _listBox;
    private Border? _dropdown;
    private Panel? _backdrop;
    private bool _closed;

    public double DropdownLeft
    {
        get => GetValue(DropdownLeftProperty);
        set => SetValue(DropdownLeftProperty, value);
    }

    public double DropdownTop
    {
        get => GetValue(DropdownTopProperty);
        set => SetValue(DropdownTopProperty, value);
    }

    public void Initialize(Panel host, SelectElementRequestEventArgs request)
    {
        _request = request;
        _host = host;

        DropdownLeft = request.PositionX;
        DropdownTop = request.PositionY + request.PositionHeight;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _backdrop = e.NameScope.Find<Panel>("PART_Backdrop");
        _dropdown = e.NameScope.Find<Border>("PART_Dropdown");
        _listBox = e.NameScope.Find<ListBox>("PART_ListBox");

        if (_backdrop != null)
            _backdrop.PointerPressed += OnBackdropPointerPressed;

        if (_listBox != null && _request != null)
        {
            _listBox.MinWidth = Math.Max(150, _request.PositionWidth);
            BuildItems();
        }

        if (_dropdown != null)
            _dropdown.LayoutUpdated += OnFirstLayout;
    }

    private void BuildItems()
    {
        if (_listBox == null || _request == null) return;

        foreach (var item in _request.Options)
        {
            if (item.IsGroup)
            {
                _listBox.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = item.GroupLabel ?? "",
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(4, 2),
                    },
                    IsEnabled = false,
                    IsHitTestVisible = false,
                });

                foreach (var opt in item.GroupOptions!)
                    AddOptionItem(opt);
            }
            else
            {
                AddOptionItem(item.Option!);
            }
        }
    }

    private void AddOptionItem(SelectOption opt)
    {
        if (_listBox == null || _request == null) return;

        var item = new ListBoxItem
        {
            Content = new TextBlock
            {
                Text = opt.Label,
                Margin = new Thickness(4, 2, 4, 2),
            },
            Tag = opt.Id,
            IsEnabled = !opt.IsDisabled,
        };

        if (_request.SelectedOptionId == opt.Id)
            item.IsSelected = true;

        if (!opt.IsDisabled)
            item.PointerReleased += OnItemPointerReleased;

        _listBox.Items.Add(item);
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is ListBoxItem { Tag: int id } && _request != null)
            Close(() => _request.Select(id));
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_request != null)
        {
            Close(() => _request.Dismiss());
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_host != null)
            _host.PropertyChanged += OnHostPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host != null)
            _host.PropertyChanged -= OnHostPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && _request != null)
            Close(() => _request.Dismiss());
    }

    private void OnFirstLayout(object? sender, EventArgs e)
    {
        if (_dropdown != null)
        {
            _dropdown.LayoutUpdated -= OnFirstLayout;
            AdjustPosition();
        }
    }

    private void AdjustPosition()
    {
        if (_host == null || _dropdown == null || _request == null) return;

        var containerH = _host.Bounds.Height;
        var containerW = _host.Bounds.Width;
        var ddH = _dropdown.DesiredSize.Height;
        var ddW = _dropdown.DesiredSize.Width;

        double dipX = _request.PositionX;
        double dipY = _request.PositionY;
        double dipH = _request.PositionHeight;

        var top = dipY + dipH;
        if (top + ddH > containerH && dipY - ddH >= 0)
            top = dipY - ddH;

        top = Math.Max(0, Math.Min(top, containerH - ddH));
        var left = Math.Max(0, Math.Min(dipX, containerW - ddW));

        DropdownLeft = left;
        DropdownTop = top;
    }

    public void DismissIfOpen()
    {
        if (_request != null)
            Close(() => _request.Dismiss());
    }

    private void Close(Action respond)
    {
        if (_closed) return;
        _closed = true;
        _host?.Children.Remove(this);
        respond();
    }
}
