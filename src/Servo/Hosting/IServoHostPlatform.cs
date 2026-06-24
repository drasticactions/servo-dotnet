namespace Servo.Hosting;

/// <summary>
/// The platform-specific seams a <see cref="ServoWebViewHost"/> needs in order to host a
/// <see cref="ServoWebView"/> inside a concrete UI framework's widget.
/// </summary>
public interface IServoHostPlatform
{
    /// <summary>
    /// Marshal <paramref name="action"/> onto the UI thread. Every host event is raised through
    /// this, so platform handlers never have to post for themselves.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Create the platform's rendering context sized to the given device-pixel dimensions.
    /// The platform owns the returned context: it presents through it (see <see cref="PresentFrame"/>)
    /// and is responsible for disposing it when the surface goes away.
    /// </summary>
    RenderingContext CreateRenderingContext(uint pixelWidth, uint pixelHeight);

    /// <summary>
    /// Present the most recently painted frame. Always invoked on the UI thread, after the host
    /// has called <see cref="ServoWebView.Paint"/>.
    /// </summary>
    void PresentFrame();

    /// <summary>
    /// Marshal a frame-render action onto the UI thread. Defaults to <see cref="Post"/>; override
    /// when the framework wants frames scheduled at a distinct priority.
    /// </summary>
    void PostRender(Action action) => Post(action);

    /// <summary>Device-pixels-per-logical-pixel for the surface (e.g. <c>NSWindow.BackingScaleFactor</c>).</summary>
    double GetScaling();

    /// <summary>Logical (unscaled) size of the host surface.</summary>
    (double Width, double Height) GetLogicalSize();

    /// <summary>The UI theme to report to the engine on init and on theme change. Defaults to light.</summary>
    ServoTheme Theme => ServoTheme.Light;
}
