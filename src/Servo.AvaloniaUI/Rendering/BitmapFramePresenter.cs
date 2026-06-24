using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Servo.AvaloniaUI.Rendering;

/// <summary>
/// CPU presenter: This blits the frame through a
/// <see cref="WriteableBitmap"/> drawn in the owner's render pass.
/// </summary>
internal sealed class BitmapFramePresenter : IServoFramePresenter
{
    private readonly Control _owner;
    private readonly RenderingContext _context;
    private WriteableBitmap? _bitmap;

    public BitmapFramePresenter(Control owner, RenderingContext context)
    {
        _owner = owner;
        _context = context;
    }

    public void PresentFrame() => _owner.InvalidateVisual();

    public unsafe void Render(DrawingContext context, Size logicalBounds)
    {
        if (_context.IsDisposed)
            return;

        var pixels = _context.ReadPixels();
        _context.Present();
        if (pixels == null || pixels.Data.Length == 0)
        {
            context.FillRectangle(Brushes.Black, new Rect(logicalBounds));
            return;
        }

        if (pixels.Data.AsSpan().IndexOfAnyExcept((byte)0) == -1 && _bitmap != null)
        {
            context.DrawImage(_bitmap, new Rect(logicalBounds));
            return;
        }

        var neededSize = new PixelSize((int)pixels.Width, (int)pixels.Height);
        if (_bitmap == null || _bitmap.PixelSize != neededSize)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                neededSize,
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Premul);
        }

        using (var fb = _bitmap.Lock())
        {
            Unsafe.CopyBlock(fb.Address.ToPointer(), Unsafe.AsPointer(ref pixels.Data[0]),
                (uint)pixels.Data.Length);
        }

        context.DrawImage(_bitmap, new Rect(logicalBounds));
    }

    public void OnBoundsChanged(Size logicalBounds)
    {
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
