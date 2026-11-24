using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Cantus.Client.Views;

/// <summary>Closes a dialog, as its close button would, when the dimmed area around it is clicked.</summary>
/// <remarks>
/// Uno sizes the dialog to its content and centres it in a popup panel that draws the dimmed area, so a
/// click outside the dialog lands on that panel, never on the dialog. The handlers sit on the shared popup
/// root above it while the dialog is open. A press and a release must both land on the dimmed area, so
/// dragging a text selection out of the dialog does not close it.
/// </remarks>
internal static class DialogLightDismiss
{
    public static void Enable(ContentDialog dialog)
    {
        UIElement? popupRoot = null;
        bool pressedOnBackdrop = false;

        PointerEventHandler pressed = (_, e) => pressedOnBackdrop = IsBackdrop(e.OriginalSource, dialog);
        PointerEventHandler released = (_, e) =>
        {
            if (pressedOnBackdrop && IsBackdrop(e.OriginalSource, dialog))
            {
                dialog.Hide();
            }

            pressedOnBackdrop = false;
        };

        dialog.Opened += (_, _) =>
        {
            popupRoot = VisualTreeHelper.GetParent(dialog) as UIElement;
            popupRoot?.AddHandler(UIElement.PointerPressedEvent, pressed, handledEventsToo: true);
            popupRoot?.AddHandler(UIElement.PointerReleasedEvent, released, handledEventsToo: true);
        };

        dialog.Closed += (_, _) =>
        {
            popupRoot?.RemoveHandler(UIElement.PointerPressedEvent, pressed);
            popupRoot?.RemoveHandler(UIElement.PointerReleasedEvent, released);
            popupRoot = null;
            pressedOnBackdrop = false;
        };
    }

    private static bool IsBackdrop(object? source, ContentDialog dialog) =>
        source is Panel panel && panel.Children.Contains(dialog);
}
