using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cantus.Client.Views;

/// <summary>Closes a dialog, as its close button would, when the dimmed area around it is tapped.</summary>
internal static class DialogLightDismiss
{
    // The dimming rectangle in the Fluent ContentDialog template; it is inside the dialog's own template,
    // so taps on it reach the dialog. A tap, unlike a press, ignores a text selection dragged out of the box.
    private const string SMOKE_LAYER_NAME = "SmokeLayerBackground";

    public static void Enable(ContentDialog dialog) =>
        dialog.AddHandler(
            UIElement.TappedEvent,
            new TappedEventHandler((_, e) =>
            {
                if (e.OriginalSource is FrameworkElement { Name: SMOKE_LAYER_NAME })
                {
                    dialog.Hide();
                }
            }),
            handledEventsToo: true);
}
