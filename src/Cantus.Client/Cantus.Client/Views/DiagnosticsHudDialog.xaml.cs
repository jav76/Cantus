using Cantus.Client.ViewModels;

namespace Cantus.Client.Views;

public sealed partial class DiagnosticsHudDialog : ContentDialog
{
    public LyricsViewModel ViewModel { get; }

    public DiagnosticsHudDialog(LyricsViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
    }

    public string GetLatencySkewText(long rtt, long skew)
    {
        string skewSign = skew >= 0 ? "+" : "";
        return $"{rtt}ms / {skewSign}{skew}ms";
    }

    public string GetPollerCadenceText(string status, int intervalMs)
    {
        return $"{status} ({intervalMs}ms)";
    }

    public string GetVolumeText(int? volume)
    {
        return volume.HasValue ? $"{volume.Value}%" : "N/A";
    }
}
