namespace Cantus.Client.Models;

/// <summary>A per-device post-processing effect, listed in menus in declaration order.</summary>
public enum VisualEffect
{
    FilmGrain,
    Vignette,
    Scanlines,
    BackgroundBlur,
}

public static class VisualEffectExtensions
{
    public static string GetDisplayName(this VisualEffect effect) => effect switch
    {
        VisualEffect.FilmGrain => "Film grain",
        VisualEffect.Vignette => "Vignette",
        VisualEffect.Scanlines => "Scanlines",
        VisualEffect.BackgroundBlur => "Blur album background (Dynamic theme)",
        _ => effect.ToString(),
    };
}
