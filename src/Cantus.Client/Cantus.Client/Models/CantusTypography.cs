namespace Cantus.Client.Models;

/// <summary>
/// Font sources for code that cannot use the Cantus*FontFamily resources in App.xaml.
/// Each weight is its own file, so text picks a weight through its family and keeps FontWeight at Normal.
/// </summary>
public static class CantusTypography
{
    public const string TEXT_REGULAR_SOURCE = "ms-appx:///Assets/Fonts/Figtree-Regular.ttf#Figtree";
    public const string DISPLAY_REGULAR_SOURCE = "ms-appx:///Assets/Fonts/Fraunces-Regular.ttf#Fraunces";
    public const string DISPLAY_SEMIBOLD_SOURCE = "ms-appx:///Assets/Fonts/Fraunces-SemiBold.ttf#Fraunces";

    /// <summary>The active lyric line is the only one set in the heavier display weight.</summary>
    public static string GetLyricLineSource(bool isActive)
        => isActive ? DISPLAY_SEMIBOLD_SOURCE : DISPLAY_REGULAR_SOURCE;
}
