using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cantus.Client.ViewModels;

public sealed class LyricWordViewModel : INotifyPropertyChanged
{
    private bool _isHovered;

    public string Word { get; init; } = string.Empty;
    public long TimestampMs { get; init; }
    public int WordIndex { get; init; }
    public LyricLineViewModel ParentLine { get; }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered != value)
            {
                _isHovered = value;
                OnPropertyChanged();
            }
        }
    }

    public LyricWordViewModel(string word, long timestampMs, int wordIndex, LyricLineViewModel parentLine)
    {
        Word = word;
        TimestampMs = timestampMs;
        WordIndex = wordIndex;
        ParentLine = parentLine;
    }

    public void Calibrate()
    {
        ParentLine.OnWordClicked(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
