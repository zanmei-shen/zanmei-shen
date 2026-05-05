using Microsoft.UI.Xaml;

namespace ScreenRecorder;

/// <summary>
/// Provides application-specific behaviour to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
