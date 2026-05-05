using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenRecorder.Capture;

/// <summary>
/// Wraps the Windows Graphics Capture API.
/// Produces a stream of <see cref="Direct3D11CaptureFrame"/> objects
/// that can be consumed by the encoding pipeline.
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;

    // Channel between the WGC callback and the async enumerator.
    private readonly System.Threading.Channels.Channel<Direct3D11CaptureFrame> _channel =
        System.Threading.Channels.Channel.CreateBounded<Direct3D11CaptureFrame>(
            new System.Threading.Channels.BoundedChannelOptions(8)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

    private bool _disposed;

    // ── Construction ──────────────────────────────────────────────────────────

    public ScreenCaptureService(GraphicsCaptureItem item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the frame pool and starts the capture session.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Create a Direct3D device shared with the capture API.
        IDirect3DDevice d3dDevice = CreateDirect3DDevice();

        SizeInt32 size = _item.Size;

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size);

        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = true;
        _session.StartCapture();
    }

    /// <summary>
    /// Stops the capture session and signals end-of-stream to the frame channel.
    /// </summary>
    public void Stop()
    {
        _session?.Dispose();
        _session = null;

        _channel.Writer.TryComplete();

        _framePool?.Dispose();
        _framePool = null;
    }

    /// <summary>
    /// Returns an async stream of captured frames.
    /// The caller is responsible for disposing each frame after use.
    /// </summary>
    public async IAsyncEnumerable<Direct3D11CaptureFrame> GetFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var frame in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is not null)
        {
            // Non-blocking write – channel is bounded, old frames are dropped when full.
            _channel.Writer.TryWrite(frame);
        }
    }

    /// <summary>
    /// Creates a <see cref="IDirect3DDevice"/> backed by the default hardware adapter.
    /// Uses the Windows.Graphics.DirectX.Direct3D11 interop helper.
    /// </summary>
    private static IDirect3DDevice CreateDirect3DDevice()
    {
        // CreateDirect3D11DeviceFromDXGIDevice is the standard way to obtain
        // an IDirect3DDevice from an existing D3D11 device without P/Invoke.
        return Direct3D11Helper.CreateDevice();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
