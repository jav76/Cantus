using System.ComponentModel;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>
/// Shows the <see cref="LyricsPickerViewModel"/> for the current track. The
/// dialog closes itself once the picker closes, after a choice is applied or
/// the track changes.
/// </summary>
public sealed partial class LyricsPickerDialog : ContentDialog
{
    public LyricsPickerViewModel Picker { get; }

    public ThemeManager Theme { get; } = ThemeManager.Instance;

    public LyricsPickerDialog(LyricsPickerViewModel picker)
    {
        Picker = picker;
        InitializeComponent();
        DialogLightDismiss.Enable(this);
        Picker.PropertyChanged += OnPickerPropertyChanged;
        Closed += OnDialogClosed;
    }

    /// <summary>Opens the picker and shows it over <paramref name="xamlRoot"/> until it closes.</summary>
    public static async Task ShowForAsync(LyricsPickerViewModel picker, XamlRoot? xamlRoot)
    {
        Task loading = picker.OpenAsync();
        if (!picker.IsOpen)
        {
            await loading;
            return;
        }

        LyricsPickerDialog dialog = new(picker);
        if (xamlRoot is not null)
        {
            dialog.XamlRoot = xamlRoot;
        }

        await dialog.ShowAsync();
        await loading;
    }

    private void OnPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsPickerViewModel.IsOpen) && !Picker.IsOpen)
        {
            Hide();
        }
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Picker.PropertyChanged -= OnPickerPropertyChanged;
        Closed -= OnDialogClosed;
        Picker.Close();
    }

    private async void OnCandidateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricsCandidateItem candidate })
        {
            await Picker.UseCandidateAsync(candidate);
        }
    }

    private async void OnRefetchClicked(object sender, RoutedEventArgs e)
    {
        await Picker.RefetchAsync();
    }

    private async void OnNoLyricsClicked(object sender, RoutedEventArgs e)
    {
        await Picker.MarkNoLyricsAsync();
    }

    private async void OnClearOverrideClicked(object sender, RoutedEventArgs e)
    {
        await Picker.ClearOverrideAsync();
    }
}
