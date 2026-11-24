using System;
using System.IO;
using Cantus.Client.Services;
using Cantus.Client.Windowing;
using Microsoft.Extensions.Logging;
using Uno.UI.NativeElementHosting;
using Uno.UI.Xaml;

namespace Cantus.Client;

public partial class App
{
    private const string WINDOW_ICON_FILE = "cantus.png";

    partial void ApplyDesktopWindowIdentity(Window window)
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, WINDOW_ICON_FILE);
            if (OperatingSystem.IsLinux())
            {
                if (WindowHelper.GetNativeWindow(window) is X11NativeWindow x11Window)
                {
                    X11WindowIdentity.Apply(x11Window.WindowId, iconPath);
                }
            }
            else if (File.Exists(iconPath))
            {
                window.AppWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            ClientLoggingManager.CreateLogger<App>().LogWarning(ex, "Could not set the window icon or class");
        }
    }
}
