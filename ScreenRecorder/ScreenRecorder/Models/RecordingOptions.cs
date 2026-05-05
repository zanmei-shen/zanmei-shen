namespace ScreenRecorder.Models;

/// <summary>
/// Encapsulates all user-configurable options for a recording session.
/// </summary>
public sealed class RecordingOptions
{
    /// <summary>Full path to the output MP4 file.</summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>Target frames per second (e.g. 24, 30, 60).</summary>
    public int FrameRate { get; init; } = 30;

    /// <summary>Target video bitrate in bits per second (e.g. 8_000_000 = 8 Mbps).</summary>
    public uint Bitrate { get; init; } = 8_000_000;

    /// <summary>
    /// Whether to capture the mouse cursor in the recording.
    /// Maps to <c>GraphicsCaptureSession.IsCursorCaptureEnabled</c>.
    /// </summary>
    public bool CaptureCursor { get; init; } = true;

    /// <summary>
    /// Whether to capture audio from the default render endpoint (desktop audio).
    /// Reserved for future use – audio mixing is not included in this version.
    /// </summary>
    public bool CaptureAudio { get; init; } = false;
}
