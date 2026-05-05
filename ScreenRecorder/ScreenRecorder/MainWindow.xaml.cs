using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenRecorder.Capture;
using ScreenRecorder.Encoding;
using ScreenRecorder.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenRecorder;

/// <summary>
/// Main application window – coordinates the UI, capture service, and video encoder.
/// </summary>
public sealed partial class MainWindow : Window
{
    // ── State ────────────────────────────────────────────────────────────────

    private ScreenCaptureService? _captureService;
    private VideoEncoder? _encoder;
    private CancellationTokenSource? _cts;
    private System.Timers.Timer? _uiTimer;
    private TimeSpan _elapsed = TimeSpan.Zero;
    private string _outputPath = string.Empty;

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        PopulateMonitorCombo();
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    private void PopulateMonitorCombo()
    {
        // GraphicsCapturePicker shows OS-level picker; list available items
        // via the display-info API so users can choose in-app first.
        var displays = Windows.Devices.Display.DisplayMonitor.FindAllAsync().AsTask().GetAwaiter().GetResult();
        foreach (var d in displays)
        {
            MonitorCombo.Items.Add(new ComboBoxItem
            {
                Content = d.DisplayName ?? $"Display {MonitorCombo.Items.Count + 1}",
                Tag = d
            });
        }
        if (MonitorCombo.Items.Count > 0)
            MonitorCombo.SelectedIndex = 0;
    }

    private static int FrameRateFromIndex(int idx) => idx switch
    {
        0 => 24,
        2 => 60,
        _ => 30,
    };

    private static uint BitrateFromIndex(int idx) => idx switch
    {
        0 => 4_000_000u,
        2 => 16_000_000u,
        _ => 8_000_000u,
    };

    private void SetRecordingState(bool recording)
    {
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        BrowseButton.IsEnabled = !recording;
        MonitorCombo.IsEnabled = !recording;
        FrameRateCombo.IsEnabled = !recording;
        BitrateCombo.IsEnabled = !recording;
        OutputPathBox.IsEnabled = !recording;
    }

    private void UpdateTimerLabel()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            TimerLabel.Text = _elapsed.ToString(@"mm\:ss");
        });
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultFileExtension = ".mp4"
        };
        picker.FileTypeChoices.Add("MP4 Video", new List<string> { ".mp4" });

        // Associate picker with the current window HWND.
        InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            _outputPath = file.Path;
            OutputPathBox.Text = _outputPath;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            await ShowErrorAsync("Please choose an output file first.");
            return;
        }

        var options = new RecordingOptions
        {
            OutputPath = _outputPath,
            FrameRate = FrameRateFromIndex(FrameRateCombo.SelectedIndex),
            Bitrate = BitrateFromIndex(BitrateCombo.SelectedIndex),
        };

        try
        {
            // Let the user pick what to capture via the system picker.
            var capturePicker = new GraphicsCapturePicker();
            InitializeWithWindow.Initialize(capturePicker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            GraphicsCaptureItem? item = await capturePicker.PickSingleItemAsync();
            if (item is null) return;   // user cancelled

            _cts = new CancellationTokenSource();
            _captureService = new ScreenCaptureService(item);
            _encoder = new VideoEncoder(options, item.Size);

            SetRecordingState(recording: true);
            _elapsed = TimeSpan.Zero;
            TimerLabel.Text = "00:00";
            PreviewPlaceholder.Text = "Recording…";

            _uiTimer = new System.Timers.Timer(1_000);
            _uiTimer.Elapsed += (_, _) => UpdateTimerLabel();
            _uiTimer.Start();

            await RecordAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal stop – do nothing.
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Recording failed:\n{ex.Message}");
        }
        finally
        {
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _uiTimer = null;
            SetRecordingState(recording: false);
            PreviewPlaceholder.Text = "Preview will appear here while recording";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) =>
        _cts?.Cancel();

    // ── Core recording loop ──────────────────────────────────────────────────

    private async Task RecordAsync(CancellationToken ct)
    {
        if (_captureService is null || _encoder is null)
            throw new InvalidOperationException("Capture service or encoder not initialised.");

        await Task.Run(async () =>
        {
            await _encoder.StartAsync();
            _captureService.Start();

            await foreach (var frame in _captureService.GetFramesAsync(ct))
            {
                await _encoder.EncodeFrameAsync(frame, ct);
                frame.Dispose();
            }

            await _encoder.StopAsync();
            _captureService.Stop();
        }, ct);
    }

    // ── Utilities ────────────────────────────────────────────────────────────

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Screen Recorder",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
