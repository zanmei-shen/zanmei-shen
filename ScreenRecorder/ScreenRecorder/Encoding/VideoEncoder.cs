using ScreenRecorder.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace ScreenRecorder.Encoding;

/// <summary>
/// Encodes captured frames to an MP4 file using Media Foundation
/// via the Windows Runtime <see cref="MediaTranscoder"/> / <see cref="MediaStreamSource"/> APIs.
/// </summary>
public sealed class VideoEncoder : IAsyncDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private readonly RecordingOptions _options;
    private readonly SizeInt32 _frameSize;

    // ── Media Foundation objects ───────────────────────────────────────────────

    private MediaStreamSource? _streamSource;
    private MediaTranscoder? _transcoder;
    private PrepareTranscodeResult? _prepareResult;
    private VideoStreamDescriptor? _videoDescriptor;

    // ── Frame timing ──────────────────────────────────────────────────────────

    private long _frameIndex;
    private readonly TimeSpan _frameDuration;

    // ── Lifecycle control ─────────────────────────────────────────────────────

    private readonly SemaphoreSlim _frameLock = new(1, 1);
    private Direct3D11CaptureFrame? _pendingFrame;
    private bool _stopped;
    private Task? _transcodeTask;

    // ── Construction ──────────────────────────────────────────────────────────

    public VideoEncoder(RecordingOptions options, SizeInt32 frameSize)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _frameSize = frameSize;
        _frameDuration = TimeSpan.FromSeconds(1.0 / options.FrameRate);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the Media Foundation pipeline and starts background transcoding.
    /// </summary>
    public Task StartAsync()
    {
        // Build encoding properties (H.264/MP4).
        var encodingProperties = VideoEncodingProperties.CreateH264();
        encodingProperties.Width = (uint)_frameSize.Width;
        encodingProperties.Height = (uint)_frameSize.Height;
        encodingProperties.FrameRate.Numerator = (uint)_options.FrameRate;
        encodingProperties.FrameRate.Denominator = 1;
        encodingProperties.Bitrate = _options.Bitrate;

        _videoDescriptor = new VideoStreamDescriptor(encodingProperties);

        // Create a MediaStreamSource that we will feed frames into.
        _streamSource = new MediaStreamSource(_videoDescriptor)
        {
            BufferTime = TimeSpan.Zero,
            IsLive = true,
        };
        _streamSource.SampleRequested += OnSampleRequested;
        _streamSource.Closed += OnStreamSourceClosed;

        // Transcode to MP4 on disk.
        _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

        var outputProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        outputProfile.Video = encodingProperties;
        outputProfile.Audio = null; // video-only

        _transcodeTask = TranscodeAsync(outputProfile);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues a captured frame for encoding.
    /// </summary>
    public async Task EncodeFrameAsync(Direct3D11CaptureFrame frame, CancellationToken ct)
    {
        await _frameLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pendingFrame?.Dispose();
            _pendingFrame = frame;
        }
        finally
        {
            _frameLock.Release();
        }
    }

    /// <summary>
    /// Signals end-of-stream and waits for the transcoder to finish.
    /// </summary>
    public async Task StopAsync()
    {
        _stopped = true;

        // Null out the stream source event handler to prevent further callbacks.
        if (_streamSource is not null)
            _streamSource.SampleRequested -= OnSampleRequested;

        if (_transcodeTask is not null)
            await _transcodeTask.ConfigureAwait(false);
    }

    // ── MediaStreamSource callbacks ───────────────────────────────────────────

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (_stopped)
        {
            // Signal end-of-stream.
            args.Request.Sample = null;
            return;
        }

        _frameLock.Wait();
        Direct3D11CaptureFrame? frame;
        try
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }
        finally
        {
            _frameLock.Release();
        }

        if (frame is null)
        {
            // No frame available yet – use the deferred object so the pipeline
            // waits briefly rather than receiving a null/invalid surface.
            var deferral = args.Request.GetDeferral();
            _ = WaitForFrameAndCompleteAsync(args.Request, deferral);
            return;
        }

        TimeSpan timestamp = TimeSpan.FromTicks(_frameIndex * _frameDuration.Ticks);
        var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timestamp);
        sample.Duration = _frameDuration;
        args.Request.Sample = sample;

        Interlocked.Increment(ref _frameIndex);
        frame.Dispose();
    }

    /// <summary>
    /// Called when no frame is immediately available. Polls briefly then either
    /// supplies the first available frame or signals end-of-stream.
    /// </summary>
    private async Task WaitForFrameAndCompleteAsync(
        MediaStreamSourceSampleRequest request,
        MediaStreamSourceSampleRequestDeferral deferral)
    {
        const int MaxWaitMs = 100;
        const int PollMs = 5;
        int waited = 0;

        try
        {
            while (waited < MaxWaitMs)
            {
                if (_stopped)
                {
                    request.Sample = null;
                    return;
                }

                await _frameLock.WaitAsync().ConfigureAwait(false);
                Direct3D11CaptureFrame? frame;
                try
                {
                    frame = _pendingFrame;
                    _pendingFrame = null;
                }
                finally
                {
                    _frameLock.Release();
                }

                if (frame is not null)
                {
                    TimeSpan timestamp = TimeSpan.FromTicks(
                        Interlocked.Read(ref _frameIndex) * _frameDuration.Ticks);
                    var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timestamp);
                    sample.Duration = _frameDuration;
                    request.Sample = sample;
                    Interlocked.Increment(ref _frameIndex);
                    frame.Dispose();
                    return;
                }

                await Task.Delay(PollMs).ConfigureAwait(false);
                waited += PollMs;
            }

            // Timed out – signal end-of-stream to avoid a pipeline stall.
            request.Sample = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnStreamSourceClosed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
    {
        _stopped = true;
    }

    // ── Internal transcoding ──────────────────────────────────────────────────

    private async Task TranscodeAsync(MediaEncodingProfile profile)
    {
        if (_streamSource is null || _transcoder is null)
            throw new InvalidOperationException("Encoder not initialised.");

        var outputFile = await Windows.Storage.StorageFile
            .GetFileFromPathAsync(_options.OutputPath)
            .AsTask()
            .ConfigureAwait(false);

        _prepareResult = await _transcoder
            .PrepareMediaStreamSourceTranscodeAsync(_streamSource, await outputFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite), profile)
            .AsTask()
            .ConfigureAwait(false);

        if (!_prepareResult.CanTranscode)
            throw new InvalidOperationException($"Transcoder cannot transcode: {_prepareResult.FailureReason}");

        await _prepareResult.TranscodeAsync().AsTask().ConfigureAwait(false);
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
            await StopAsync().ConfigureAwait(false);

        _frameLock.Dispose();
        _pendingFrame?.Dispose();
    }
}
