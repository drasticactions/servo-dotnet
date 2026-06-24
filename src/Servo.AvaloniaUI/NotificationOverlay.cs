using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Servo.AvaloniaUI;

[TemplatePart("PART_CloseButton", typeof(Button))]
public class NotificationOverlay : TemplatedControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<NotificationOverlay, string>(nameof(TitleText), "");

    public static readonly StyledProperty<string> BodyTextProperty =
        AvaloniaProperty.Register<NotificationOverlay, string>(nameof(BodyText), "");

    private Panel? _host;
    private bool _closed;
    private DispatcherTimer? _timer;

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string BodyText
    {
        get => GetValue(BodyTextProperty);
        set => SetValue(BodyTextProperty, value);
    }

    public void Initialize(Panel host, NotificationEventArgs args, TimeSpan? duration = null)
    {
        _host = host;
        TitleText = args.Title;
        BodyText = args.Body;

        _timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var closeBtn = e.NameScope.Find<Button>("PART_CloseButton");
        if (closeBtn != null)
            closeBtn.Click += (_, _) => Close();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _timer?.Stop();
        _timer = null;
        _host?.Children.Remove(this);
    }
}
