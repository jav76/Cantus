using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Cantus.Client.Services;

namespace Cantus.Client.ViewModels;

/// <summary>One of the owner's Displays as a row in the Displays list.</summary>
public sealed class DisplayItemViewModel : INotifyPropertyChanged
{
    public const string NEVER_SEEN_TEXT = "Not connected yet";
    public const string JUST_NOW_TEXT = "Last seen just now";

    private const int RELATIVE_DAYS_LIMIT = 7;
    private const string ABSOLUTE_DATE_FORMAT = "d MMM yyyy";

    private string _name;
    private DateTimeOffset? _lastSeenAtUtc;
    private string _lastSeenText;

    public DisplayItemViewModel(DisplayInfoPayload display, DateTimeOffset nowUtc)
    {
        Id = display.Id;
        CreatedAtUtc = display.CreatedAtUtc;
        _name = display.Name;
        _lastSeenAtUtc = display.LastSeenAtUtc;
        _lastSeenText = FormatLastSeen(display.LastSeenAtUtc, nowUtc);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string Name
    {
        get => _name;
        internal set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset? LastSeenAtUtc => _lastSeenAtUtc;

    public string LastSeenText => _lastSeenText;

    /// <summary>How long ago a Display last connected, in the owner's words.</summary>
    public static string FormatLastSeen(DateTimeOffset? lastSeenUtc, DateTimeOffset nowUtc)
    {
        if (lastSeenUtc is null)
        {
            return NEVER_SEEN_TEXT;
        }

        TimeSpan age = nowUtc - lastSeenUtc.Value;
        if (age < TimeSpan.FromMinutes(1))
        {
            return JUST_NOW_TEXT;
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Last seen {(int)age.TotalMinutes} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"Last seen {(int)age.TotalHours} h ago";
        }

        if (age < TimeSpan.FromDays(RELATIVE_DAYS_LIMIT))
        {
            int days = (int)age.TotalDays;
            return days == 1 ? "Last seen 1 day ago" : $"Last seen {days} days ago";
        }

        string date = lastSeenUtc.Value.ToLocalTime().ToString(ABSOLUTE_DATE_FORMAT, CultureInfo.InvariantCulture);
        return $"Last seen {date}";
    }

    internal void Update(DisplayInfoPayload display, DateTimeOffset nowUtc)
    {
        Name = display.Name;
        _lastSeenAtUtc = display.LastSeenAtUtc;
        string lastSeenText = FormatLastSeen(display.LastSeenAtUtc, nowUtc);
        if (_lastSeenText != lastSeenText)
        {
            _lastSeenText = lastSeenText;
            OnPropertyChanged(nameof(LastSeenText));
        }
        OnPropertyChanged(nameof(LastSeenAtUtc));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
