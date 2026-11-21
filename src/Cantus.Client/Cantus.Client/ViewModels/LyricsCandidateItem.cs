using System;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>One lyrics candidate as the picker lists it.</summary>
public sealed class LyricsCandidateItem
{
    private const string UNKNOWN_DURATION_TEXT = "--:--";
    private const string DETAIL_SEPARATOR = " · ";

    public LyricsCandidateItem(LyricsCandidatePayload payload, bool isActive)
    {
        Provider = payload.Provider;
        CandidateId = payload.ProviderCandidateId;
        Title = payload.Title;
        Artist = payload.Artist;
        Album = payload.Album;
        IsSynced = payload.IsSynced;
        IsInstrumental = payload.IsInstrumental;
        IsActive = isActive;
        DurationText = FormatDuration(payload.DurationMs);
        KindText = payload.IsInstrumental ? "Instrumental" : payload.IsSynced ? "Synced" : "Plain";
        PreviewText = string.Join("\n", payload.Preview ?? new());
    }

    public string Provider { get; }

    public string CandidateId { get; }

    public string Title { get; }

    public string Artist { get; }

    public string? Album { get; }

    public bool IsSynced { get; }

    public bool IsInstrumental { get; }

    /// <summary>Whether this is the candidate the owner already picked for the track.</summary>
    public bool IsActive { get; }

    public string DurationText { get; }

    public string KindText { get; }

    public string DetailText => string.Join(DETAIL_SEPARATOR, Artist, DurationText, KindText);

    public string PreviewText { get; }

    public Visibility PreviewVisibility =>
        string.IsNullOrWhiteSpace(PreviewText) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatDuration(long durationMs)
    {
        if (durationMs <= 0)
        {
            return UNKNOWN_DURATION_TEXT;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
        return $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
    }
}
