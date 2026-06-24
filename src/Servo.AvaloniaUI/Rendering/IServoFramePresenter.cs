using Avalonia;
using Avalonia.Media;

namespace Servo.AvaloniaUI.Rendering;

/// <summary>
/// Strategy for getting frames painted into a <see cref="RenderingContext"/> onto the
/// screen. Owned by <see cref="ServoSurface"/>; all members are called on the UI thread.
/// </summary>
internal interface IServoFramePresenter : IDisposable
{
    /// <summary>
    /// Present the most recently painted frame. Invoked (via
    /// <see cref="Servo.Hosting.IServoHostPlatform.PresentFrame"/>) after the host has
    /// called <see cref="ServoWebView.Paint"/>.
    /// </summary>
    void PresentFrame();

    /// <summary>
    /// The owner control's render pass. Only presenters that draw through the owner's
    /// visual do work here; GPU presenters output through a child
    /// composition visual instead.
    /// </summary>
    void Render(DrawingContext context, Size logicalBounds);

    /// <summary>The owner control's logical bounds changed.</summary>
    void OnBoundsChanged(Size logicalBounds);
}
