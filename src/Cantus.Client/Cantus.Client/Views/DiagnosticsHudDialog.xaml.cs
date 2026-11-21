using Cantus.Client.Services;
using Cantus.Client.ViewModels;

namespace Cantus.Client.Views;

public sealed partial class DiagnosticsHudDialog : ContentDialog
{
    public LyricsViewModel ViewModel { get; }

    public PlaybackTransport Transport { get; }

    public PlaybackTiming Timing { get; }

    public SessionViewModel Session { get; }

    public ThemeManager Theme { get; }

    public DiagnosticsHudDialog(LyricsViewModel viewModel)
    {
        ViewModel = viewModel;
        Transport = viewModel.Transport;
        Timing = viewModel.Timing;
        Session = viewModel.Session;
        Theme = viewModel.Theme;
        this.InitializeComponent();
    }

    public string GetLatencySkewText(long? rtt = null, long? skew = null)
    {
        long r = rtt.GetValueOrDefault();
        long s = skew.GetValueOrDefault();
        string skewSign = s >= 0 ? "+" : "";
        return $"{r}ms / {skewSign}{s}ms";
    }

    public string GetPollerCadenceText(string? status = null, int? intervalMs = null)
    {
        string st = !string.IsNullOrEmpty(status) ? status : "Idle";
        int ms = intervalMs.GetValueOrDefault(1500);
        return $"{st} ({ms}ms)";
    }

    public string GetVolumeText(int? volume = null)
    {
        return volume.HasValue ? $"{volume.Value}%" : "N/A";
    }
}
