using System;
using System.Threading.Tasks;
using Cantus.Core.Logging;
using Cantus.Infrastructure.Logging;
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

    public static void InitializeLogging(LoggingConfiguration configuration = CantusLoggingManager.DEFAULT_CONFIGURATION)
    {
        try
        {
            ILoggerFactory factory = LoggerFactory.Create(builder =>
            {
#if __WASM__
                builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#else
                builder.AddProvider(new Log4NetLoggerProvider());
                builder.AddConsole();
#endif
                LogLevel minLevel = configuration switch
                {
                    LoggingConfiguration.None => LogLevel.Information,
                    LoggingConfiguration.Debug => LogLevel.Debug,
                    LoggingConfiguration.Trace => LogLevel.Trace,
                    _ => LogLevel.Information
                };

                builder.SetMinimumLevel(minLevel);
                builder.AddFilter("Uno", LogLevel.Warning);
                builder.AddFilter("Windows", LogLevel.Warning);
                builder.AddFilter("Microsoft", LogLevel.Warning);
            });

            global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
            global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif

            // Global client unhandled exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    ILogger logger = factory.CreateLogger("Cantus.Client.App");
                    logger.LogError(ex, "Unhandled AppDomain exception occurred in client.");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                ILogger logger = factory.CreateLogger("Cantus.Client.App");
                logger.LogError(e.Exception, "Unobserved task exception occurred in client.");
                e.SetObserved();
            };
        }
        catch
        {
        }
    }
}
