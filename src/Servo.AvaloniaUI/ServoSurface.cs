using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Servo.AvaloniaUI.Rendering;

namespace Servo.AvaloniaUI;

/// <summary>
/// The control that shows Servo's output. Presentation is delegated to an
/// <see cref="IServoFramePresenter"/>: the GPU <see cref="CompositionFramePresenter"/>
/// when the rendering context and compositor support it, otherwise the CPU
/// <see cref="BitmapFramePresenter"/>.
/// </summary>
internal class ServoSurface : Control
{
    private RenderingContext? _renderingContext;
    private IServoFramePresenter? _presenter;

    public void SetRenderingContext(RenderingContext context)
    {
        _presenter?.Dispose();
        _renderingContext = context;
        // Start on the CPU fallback so frames present immediately; upgrade to the GPU
        // presenter once its async capability checks pass.
        _presenter = new BitmapFramePresenter(this, context);
        TryActivateComposition(context);
    }

    public void MarkFrameReady() => _presenter?.PresentFrame();

    private async void TryActivateComposition(RenderingContext context)
    {
        CompositionFramePresenter? presenter;
        try
        {
            presenter = await CompositionFramePresenter.TryCreateAsync(this, context);
        }
        catch (Exception)
        {
            return;
        }
        if (presenter == null)
            return;

        // The surface may have been torn down or re-targeted while the async checks ran.
        if (_renderingContext != context || _presenter is not BitmapFramePresenter)
        {
            presenter.Dispose();
            return;
        }

        presenter.DeviceLost += OnCompositionDeviceLost;
        _presenter.Dispose();
        _presenter = presenter;
        Avalonia.Logging.Logger.TryGet(Avalonia.Logging.LogEventLevel.Information, "Servo")
            ?.Log(this, "GPU composition presenter active");
    }

    private void OnCompositionDeviceLost(object? sender, EventArgs e)
    {
        if (sender is not CompositionFramePresenter presenter || _presenter != presenter)
            return;

        // Fall back to the CPU path, then retry composition against the new device.
        presenter.DeviceLost -= OnCompositionDeviceLost;
        presenter.Dispose();
        if (_renderingContext is not { } context || context.IsDisposed)
        {
            _presenter = null;
            return;
        }
        _presenter = new BitmapFramePresenter(this, context);
        TryActivateComposition(context);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_presenter is { } presenter)
            presenter.Render(context, Bounds.Size);
        else
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            _presenter?.OnBoundsChanged(Bounds.Size);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _presenter?.Dispose();
        _presenter = null;
        _renderingContext = null;
        base.OnDetachedFromVisualTree(e);
    }
}
