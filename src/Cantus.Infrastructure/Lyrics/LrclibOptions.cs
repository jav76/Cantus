namespace Cantus.Infrastructure.Lyrics;

public sealed class LrclibOptions
{
    public const string SECTION_NAME = "Lrclib";
    public string BaseUrl { get; set; } = "https://lrclib.net";
    public string UserAgent { get; set; } = "Cantus/1.0 (https://github.com/jaret/Cantus)";
    public int TimeoutSeconds { get; set; } = 10;
    public int NegativeCacheDays { get; set; } = 7;
}
