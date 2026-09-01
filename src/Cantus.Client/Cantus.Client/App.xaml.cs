using System;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Client;

public partial class App : Application
{
    public App()
    {
        InitializeLogging();
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        MainWindow ??= Microsoft.UI.Xaml.Window.Current ?? new Window();

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content is null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

#if !__WASM__
        MainWindow.Title = "Cantus - Real-Time Spotify Synced Lyrics";
#endif
        MainWindow.Activate();
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    public static void InitializeLogging(LoggingConfiguration configuration = ClientLoggingManager.DEFAULT_CONFIGURATION)
    {
        try
        {
            ILoggerFactory factory = ClientLoggingManager.InitializeLogging(configuration);
#if HAS_UNO
            global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
            global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
        }
        catch
        {
        }
    }
}
