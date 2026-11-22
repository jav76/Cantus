using System;
using System.ComponentModel;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Cantus.Client.Views;

/// <summary>The page-level prompt that approves a Display's code, renames a Display or removes one.</summary>
public sealed partial class DisplayPromptView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(DisplayPromptView),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty DisplaysProperty =
        DependencyProperty.Register(
            nameof(Displays),
            typeof(DisplaysViewModel),
            typeof(DisplayPromptView),
            new PropertyMetadata(null));

    public DisplaysViewModel? Displays
    {
        get => (DisplaysViewModel?)GetValue(DisplaysProperty);
        private set => SetValue(DisplaysProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(DisplayPromptView),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    public string SignInHint => DisplaysViewModel.SIGN_IN_HINT;

    public int MaxNameLength => DisplaysViewModel.MAX_NAME_LENGTH;

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DisplayPromptView prompt = (DisplayPromptView)d;
        if (prompt.Displays is not null)
        {
            prompt.Displays.PropertyChanged -= prompt.OnDisplaysPropertyChanged;
        }

        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        prompt.Displays = viewModel?.Session.Displays;
        prompt.Theme = viewModel?.Theme;

        if (prompt.Displays is not null)
        {
            prompt.Displays.PropertyChanged += prompt.OnDisplaysPropertyChanged;
        }
    }

    public DisplayPromptView()
    {
        InitializeComponent();
    }

    private void OnDisplaysPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DisplaysViewModel.PromptMode) || Displays is null)
        {
            return;
        }

        // Focus moves once the prompt's fields are laid out.
        DisplayPromptMode mode = Displays.PromptMode;
        bool hasCode = !string.IsNullOrEmpty(Displays.PromptCode);
        DispatcherQueue?.TryEnqueue(() => FocusFirstField(mode, hasCode));
    }

    private void FocusFirstField(DisplayPromptMode mode, bool hasCode)
    {
        switch (mode)
        {
            case DisplayPromptMode.Approve:
                (hasCode ? NameBox : CodeBox).Focus(FocusState.Programmatic);
                break;
            case DisplayPromptMode.Rename:
                NameBox.Focus(FocusState.Programmatic);
                NameBox.SelectAll();
                break;
            case DisplayPromptMode.Revoke:
                CancelButton.Focus(FocusState.Programmatic);
                break;
        }
    }

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (Displays is not null)
        {
            Displays.PromptCode = CodeBox.Text;
        }
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (Displays is not null)
        {
            Displays.PromptName = NameBox.Text;
        }
    }

    // On the root, so Escape works wherever focus is inside the prompt, buttons included.
    // Enter confirms only from a field, so it still activates a focused Cancel button.
    private async void OnPromptKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && e.OriginalSource is TextBox && Displays is not null && Displays.CanConfirm)
        {
            e.Handled = true;
            await Displays.ConfirmPromptAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Displays?.CancelPrompt();
        }
    }

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
        await SpotifySignIn.StartAsync(ViewModel?.Session, XamlRoot, () => Displays?.KeepCodeThroughSignIn());
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Displays?.CancelPrompt();
    }

    private async void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (Displays is not null)
        {
            await Displays.ConfirmPromptAsync();
        }
    }
}
