# Screen Recorder — Assets

Place the following files here before building:

| File | Size | Purpose |
|------|------|---------|
| `AppIcon.ico` | 256×256 | Application icon (ICO format, multi-resolution) |
| `AppIcon.png` | 256×256 | Source for the ICO, used in About dialogs |
| `SplashScreen.png` | 620×300 | Optional splash screen |

You can generate `AppIcon.ico` from any PNG with tools such as:

```
magick convert AppIcon.png -define icon:auto-resize="256,128,96,64,48,32,16" AppIcon.ico
```
