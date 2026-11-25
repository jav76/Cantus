using System;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Client;

public partial class App : Application
{
#if HAS_UNO
    // Runs when Program.Main first touches App, before Uno reads the default font into TextBlock's metadata.
    static App()
    {
        Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = CantusTypography.TEXT_REGULAR_SOURCE;
    }
#endif

    public App()
    {
        if (!ClientLoggingManager.IsInitialized)
        {
            InitializeLogging();
        }

        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        ThemeManager.Instance.ApplyToResources(Resources);

        MainWindow ??= Microsoft.UI.Xaml.Window.Current ?? new Window();

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content is null)
        {
            rootFrame.Navigate(GetStartPage(), args.Arguments);
        }

#if !__WASM__
        MainWindow.Title = "Cantus - Real-Time Synced Lyrics";
        ApplyMinimumWindowSize(MainWindow);
#endif
        ApplyDesktopWindowIdentity(MainWindow);
        MainWindow.Activate();
    }

    /// <summary>
    /// A desktop app with no server chosen yet opens on the server prompt. The WASM client is
    /// served by its server, so it never asks.
    /// </summary>
    private static Type GetStartPage()
    {
#if __WASM__
        return typeof(MainPage);
#else
        ServerSelection selection = ClientStartupOptions.ResolveServer(new ServerSettingsStore(new LocalSettingsStore()));

        return selection.NeedsSetup ? typeof(Views.ServerSetupPage) : typeof(MainPage);
#endif
    }

    /// <summary>Sets the window icon and the app's window class. Implemented by the desktop head only.</summary>
    partial void ApplyDesktopWindowIdentity(Window window);

#if !__WASM__
    private static void ApplyMinimumWindowSize(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = ResponsiveLayoutManager.MIN_WINDOW_WIDTH;
                presenter.PreferredMinimumHeight = ResponsiveLayoutManager.MIN_WINDOW_HEIGHT;
            }
        }
        catch (Exception ex)
        {
            ClientLoggingManager.CreateLogger<App>().LogWarning(ex, "Could not set the minimum window size");
        }
    }
#endif

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
