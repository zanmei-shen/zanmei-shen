# 🎬 Screen Recorder

A Windows desktop application built with **WinUI 3** that captures any display or window and encodes it to an **MP4** file.

---

## Features

| Feature | Details |
|---------|---------|
| Capture source | Any monitor, application window, or browser tab via the OS picker |
| Video format | H.264 / MP4 |
| Frame rates | 24 fps · 30 fps · 60 fps |
| Bitrates | 4 Mbps · 8 Mbps · 16 Mbps |
| Cursor capture | Optional (on by default) |
| Hardware encoding | Uses hardware H.264 encoder when available |

---

## Technology

| Layer | Technology |
|-------|-----------|
| UI | **WinUI 3** (Windows App SDK 1.5) |
| Screen capture | **Windows Graphics Capture API** (`Windows.Graphics.Capture`) |
| Video encoding | **Media Foundation** via WinRT (`Windows.Media.Transcoding`, `Windows.Media.Core`) |
| GPU interop | **Direct3D 11** via `Windows.Graphics.DirectX.Direct3D11` |

---

## Requirements

| Requirement | Minimum |
|-------------|---------|
| OS | Windows 10 version 1903 (build 18362) or later |
| Runtime | .NET 8 |
| SDK | Windows App SDK 1.5 |
| IDE | Visual Studio 2022 17.8+ **or** VS Code with C# Dev Kit |
| Build tools | Windows 10/11 SDK (22621) |

---

## Project structure

```
ScreenRecorder/
├── ScreenRecorder.sln          # Solution file
└── ScreenRecorder/
    ├── ScreenRecorder.csproj   # Project (net8.0-windows10.0.22621.0)
    ├── app.manifest            # DPI awareness + OS compatibility
    ├── App.xaml / .cs          # WinUI application entry point
    ├── MainWindow.xaml / .cs   # Main UI window
    ├── Assets/                 # Icons and splash screen
    ├── Capture/
    │   ├── ScreenCaptureService.cs   # Windows Graphics Capture API wrapper
    │   └── Direct3D11Helper.cs       # D3D11 device creation helper
    ├── Encoding/
    │   └── VideoEncoder.cs           # Media Foundation MP4 encoder
    └── Models/
        └── RecordingOptions.cs       # Recording configuration model
```

---

## Building

### Option A — Visual Studio 2022

1. Open `ScreenRecorder/ScreenRecorder.sln` in Visual Studio 2022.
2. Select **x64** as the target platform (ARM64 also supported but not tested).
3. Press **F5** to build and run, or **Ctrl+Shift+B** to build only.

### Option B — .NET CLI

```powershell
# From the repository root
cd ScreenRecorder/ScreenRecorder

dotnet restore
dotnet build -c Release -r win-x64
dotnet run   -c Release -r win-x64
```

> **Note:** The app must be run on Windows 10 1903+ because it depends on
> `Windows.Graphics.Capture`, which is only available from that version onward.

---

## Running

1. Launch `ScreenRecorder.exe`.
2. (Optional) Select a **Display** from the drop-down.
3. Choose **Frame rate** and **Bitrate**.
4. Click **Browse…** to pick where the MP4 file will be saved.
5. Click **▶ Start Recording** — the OS picker appears; select a window or monitor.
6. Click **⏹ Stop** when you are done.

The MP4 file is written to the path you chose.

---

## Publishing (self-contained)

```powershell
dotnet publish ScreenRecorder/ScreenRecorder/ScreenRecorder.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -o ./publish/win-x64
```

---

## Known Limitations

* **Audio** is not captured in this version. Adding desktop audio via WASAPI loopback capture is planned.
* The **live preview** pane is a placeholder; a real-time SwapChain panel preview requires additional D3D/DXGI work.
* Packaging as MSIX (Store submission) requires code-signing and a `Package.appxmanifest`.

---

## License

MIT — see [LICENSE](../LICENSE) if present.
