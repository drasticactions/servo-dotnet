using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace Servo.AvaloniaUI.Rendering;

/// <summary>
/// GPU presenter: takes each frame out of the rendering context's swap chain as a
/// native surface and imports it into <see cref="ICompositionGpuInterop"/>,
/// and presents it on a <see cref="CompositionSurfaceVisual"/> attached under the
/// owner control.
internal sealed class CompositionFramePresenter : IServoFramePresenter
{
    private const string LogArea = "Servo";

    private readonly Control _owner;
    private readonly RenderingContext _context;
    private readonly ICompositionGpuInterop _interop;
    private readonly CompositionDrawingSurface _drawingSurface;
    private readonly CompositionSurfaceVisual _visual;
    private readonly ServoFrameExportKind _exportKind;
    private readonly string _imageHandleType;
    private readonly ServoTimelineSemaphore? _frameReadySem; // producer → compositor: frame N rendered
    private readonly ServoTimelineSemaphore? _frameDoneSem;  // compositor → producer: frame N consumed
    private readonly ICompositionImportedGpuSemaphore? _readyImported;
    private readonly ICompositionImportedGpuSemaphore? _doneImported;

    private readonly Dictionary<(nint Handle, uint Width, uint Height), ICompositionImportedGpuImage> _importedImages = new();
    private readonly List<(ulong FrameId, ulong Value)> _pendingRelease = new();
    private ulong _frameCounter;
    private bool _updateInFlight;
    private ServoFrame? _queuedFrame;
    private (uint Width, uint Height) _lastFrameSize;
    private bool _deviceLostRaised;
    private bool _disposed;

    /// <summary>
    /// Raised once when the compositor's GPU device is lost. The owner should swap
    /// back to a fallback presenter and may retry composition.
    /// </summary>
    public event EventHandler? DeviceLost;

    /// <summary>
    /// Create a presenter when the rendering context exports frames the current
    /// compositor backend can import and fence; returns null otherwise.
    /// </summary>
    public static async Task<CompositionFramePresenter?> TryCreateAsync(Control owner, RenderingContext context)
    {
        var exportKind = context.FrameExportKind;
        var imageHandleType = exportKind switch
        {
            ServoFrameExportKind.IOSurface => KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef,
            ServoFrameExportKind.D3D11SharedHandle => KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle,
            _ => null,
        };
        if (imageHandleType == null)
            return null;
        if (ElementComposition.GetElementVisual(owner) is not { } elementVisual)
            return null;

        var compositor = elementVisual.Compositor;
        var interop = await compositor.TryGetCompositionGpuInterop();
        if (interop == null || !interop.SupportedImageHandleTypes.Contains(imageHandleType))
            return null;

        var sync = interop.GetSynchronizationCapabilities(imageHandleType);
        ServoTimelineSemaphore? ready = null;
        ServoTimelineSemaphore? done = null;
        if (exportKind == ServoFrameExportKind.IOSurface)
        {
            if (!sync.HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.TimelineSemaphores) ||
                !interop.SupportedSemaphoreTypes.Contains(KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent))
            {
                Logger.TryGet(LogEventLevel.Information, LogArea)?.Log(owner,
                    "Compositor lacks timeline-semaphore support ({Caps}); using bitmap presenter", sync);
                return null;
            }

            ready = ServoTimelineSemaphore.TryCreate();
            done = ServoTimelineSemaphore.TryCreate();
            if (ready == null || done == null)
            {
                ready?.Dispose();
                done?.Dispose();
                return null;
            }
        }
        else if (!sync.HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex))
        {
            Logger.TryGet(LogEventLevel.Information, LogArea)?.Log(owner,
                "Compositor lacks keyed-mutex support ({Caps}); using bitmap presenter", sync);
            return null;
        }

        try
        {
            return new CompositionFramePresenter(
                owner, context, compositor, interop, exportKind, imageHandleType, ready, done);
        }
        catch (Exception ex)
        {
            Logger.TryGet(LogEventLevel.Warning, LogArea)?.Log(owner,
                "Composition presenter initialization failed; using bitmap presenter: {Exception}", ex);
            ready?.Dispose();
            done?.Dispose();
            return null;
        }
    }

    private CompositionFramePresenter(
        Control owner,
        RenderingContext context,
        Compositor compositor,
        ICompositionGpuInterop interop,
        ServoFrameExportKind exportKind,
        string imageHandleType,
        ServoTimelineSemaphore? frameReadySem,
        ServoTimelineSemaphore? frameDoneSem)
    {
        _owner = owner;
        _context = context;
        _interop = interop;
        _exportKind = exportKind;
        _imageHandleType = imageHandleType;
        _frameReadySem = frameReadySem;
        _frameDoneSem = frameDoneSem;

        if (frameReadySem != null && frameDoneSem != null)
        {
            _readyImported = interop.ImportSemaphore(new PlatformHandle(
                frameReadySem.Handle, KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent));
            _doneImported = interop.ImportSemaphore(new PlatformHandle(
                frameDoneSem.Handle, KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent));
        }

        _drawingSurface = compositor.CreateDrawingSurface();
        _visual = compositor.CreateSurfaceVisual();
        _visual.Size = new(owner.Bounds.Width, owner.Bounds.Height);
        _visual.Surface = _drawingSurface;
        ElementComposition.SetElementChildVisual(owner, _visual);
    }

    public void PresentFrame()
    {
        if (_disposed || _context.IsDisposed)
            return;

        _context.Present();
        if (!_context.TryAcquireFrame(out var frame))
            return;

        if (_updateInFlight)
        {
            if (_queuedFrame is { } superseded)
                _context.ReleaseFrame(superseded.Id);
            _queuedFrame = frame;
            return;
        }

        SubmitFrame(frame);
    }

    public void Render(DrawingContext context, Size logicalBounds)
    {
    }

    public void OnBoundsChanged(Size logicalBounds)
    {
        if (!_disposed)
            _visual.Size = new(logicalBounds.Width, logicalBounds.Height);
    }

    private async void SubmitFrame(ServoFrame frame)
    {
        _updateInFlight = true;
        var presented = false;
        try
        {
            var image = GetOrImportImage(frame);

            if (_exportKind == ServoFrameExportKind.D3D11SharedHandle)
            {
                await _drawingSurface.UpdateWithKeyedMutexAsync(image, 0, 0);
                presented = true;
                _context.ReleaseFrame(frame.Id);
            }
            else
            {
                var value = ++_frameCounter;
                _context.SignalAfterGpuWork(_frameReadySem!, value);
                await _drawingSurface.UpdateWithTimelineSemaphoresAsync(
                    image, _readyImported!, value, _doneImported!, value);
                _pendingRelease.Add((frame.Id, value));
                presented = true;
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                Logger.TryGet(LogEventLevel.Warning, LogArea)?.Log(_owner,
                    "Composition frame update failed: {Exception}", ex);
                if (_interop.IsLost && !_deviceLostRaised)
                {
                    _deviceLostRaised = true;
                    DeviceLost?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            _updateInFlight = false;
            if (!presented)
                _context.ReleaseFrame(frame.Id);
            ReleaseConsumedFrames();

            if (_queuedFrame is { } next)
            {
                _queuedFrame = null;
                if (!_disposed && !_deviceLostRaised)
                    SubmitFrame(next);
                else
                    _context.ReleaseFrame(next.Id);
            }
        }
    }

    private ICompositionImportedGpuImage GetOrImportImage(ServoFrame frame)
    {
        if (_lastFrameSize != (frame.Width, frame.Height))
        {
            PurgeImportedImages(keepSize: (frame.Width, frame.Height));
            _lastFrameSize = (frame.Width, frame.Height);
        }

        var key = (frame.NativeHandle, frame.Width, frame.Height);
        if (!_importedImages.TryGetValue(key, out var image))
        {
            image = _interop.ImportImage(
                new PlatformHandle(frame.NativeHandle, _imageHandleType),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = (int)frame.Width,
                    Height = (int)frame.Height,
                    // IOSurfaces are BGRA and ANGLE pbuffer textures are RGBA.
                    Format = _exportKind == ServoFrameExportKind.D3D11SharedHandle
                        ? PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm
                        : PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                    // GL renders bottom-up
                    TopLeftOrigin = false,
                });
            _importedImages[key] = image;
        }

        return image;
    }

    private void PurgeImportedImages((uint Width, uint Height)? keepSize)
    {
        List<(nint, uint, uint)>? stale = null;
        foreach (var (key, image) in _importedImages)
        {
            if (keepSize is { } keep && (key.Width, key.Height) == keep)
                continue;
            _ = image.DisposeAsync();
            (stale ??= new()).Add(key);
        }
        if (stale != null)
            foreach (var key in stale)
                _importedImages.Remove(key);
    }

    /// <summary>
    /// Return every frame the compositor has finished reading 
    /// to the swap chain.
    /// </summary>
    private void ReleaseConsumedFrames()
    {
        if (_pendingRelease.Count == 0 || _frameDoneSem == null)
            return;
        var signaled = _frameDoneSem.SignaledValue;
        for (var i = _pendingRelease.Count - 1; i >= 0; i--)
        {
            if (_pendingRelease[i].Value <= signaled)
            {
                _context.ReleaseFrame(_pendingRelease[i].FrameId);
                _pendingRelease.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ElementComposition.SetElementChildVisual(_owner, null);
        _drawingSurface.Dispose();
        PurgeImportedImages(keepSize: null);

        if (_queuedFrame is { } queued)
            _context.ReleaseFrame(queued.Id);
        _queuedFrame = null;
        foreach (var (frameId, _) in _pendingRelease)
            _context.ReleaseFrame(frameId);
        _pendingRelease.Clear();

        _ = _readyImported?.DisposeAsync();
        _ = _doneImported?.DisposeAsync();
        _frameReadySem?.Dispose();
        _frameDoneSem?.Dispose();
    }
}
