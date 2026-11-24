using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Levels for the seek bar VU meter. The playback clock calls
/// <see cref="Update"/> every frame; it produces new levels at most every
/// <see cref="FRAME_INTERVAL_MS"/> and raises <see cref="LevelsUpdated"/>.
/// Levels only ever come from the selected live source while it hears sound;
/// without one the bars fall to rest and the seek bar stays plain.
/// Levels also fall to rest when playback pauses or while the pointer is over
/// the seek bar, and stop updating entirely once at rest.
/// </summary>
public sealed class VuMeterViewModel : INotifyPropertyChanged, IDisposable
{
    public const int BAND_COUNT = 48;
    public const long FRAME_INTERVAL_MS = 33;

    /// <summary>How long a live source may stay silent before the meter counts as waiting for sound.</summary>
    public const long SILENCE_FALLBACK_MS = 1500;

    /// <summary>A live source that sends nothing for this long counts as stalled.</summary>
    public const long STALL_MS = 500;

    /// <summary>Bars at or below this level are fully transparent, so quiet frequencies leave the seek bar clear.</summary>
    public const float INVISIBLE_LEVEL = 0.08f;

    /// <summary>Bars reach full opacity at this level.</summary>
    public const float OPAQUE_LEVEL = 0.25f;

    internal const float SILENCE_LEVEL = 0.05f;
    internal const float REST_LEVEL = 0.002f;
    private const float ATTACK = 0.6f;
    private const float RELEASE = 0.2f;

    private readonly IClientPreferences _preferences;
    private readonly IAudioLevelSourceProvider _provider;
    private readonly ILogger _logger;
    private readonly float[] _levels = new float[BAND_COUNT];
    private readonly float[] _target = new float[BAND_COUNT];
    private readonly float[] _live = new float[BAND_COUNT];

    private bool _isEnabled;
    private bool _isPointerOver;
    private VuMeterSource _selectedSource;
    private IAudioLevelSource? _source;
    private CancellationTokenSource? _startCts;
    private AudioLevelSourceStartResult? _startResult;
    private bool _sourceEnded;
    private long _lastFrameMs = long.MinValue;
    private long _lastLiveDataMs = long.MinValue;
    private long _lastSoundMs = long.MinValue;
    private bool _isAtRest = true;
    private VuMeterMode _mode = VuMeterMode.Static;
    private string _statusText = string.Empty;

    public VuMeterViewModel(
        IClientPreferences preferences,
        IAudioLevelSourceProvider? provider = null,
        ILogger? logger = null)
    {
        _preferences = preferences;
        _provider = provider ?? NoAudioLevelSourceProvider.Instance;
        _logger = logger ?? NullLogger.Instance;
        _isEnabled = preferences.IsVuMeterEnabled;

        VuMeterSource? stored = preferences.VuMeterSource;
        _selectedSource = stored is VuMeterSource source && IndexOf(source) >= 0
            ? source
            : _provider.DefaultSource;
        _statusText = BuildStatusText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread whenever <see cref="Levels"/> changed.</summary>
    public event EventHandler? LevelsUpdated;

    public IReadOnlyList<VuMeterSource> AvailableSources => _provider.AvailableSources;

    public IReadOnlyList<string> AvailableSourceNames
    {
        get
        {
            string[] names = new string[AvailableSources.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = AvailableSources[i].GetDisplayName();
            }

            return names;
        }
    }

    /// <summary>True when this platform has at least one real audio source to pick.</summary>
    public bool HasLiveSources => AvailableSources.Count > 1;

    public ReadOnlySpan<float> Levels => _levels;

    /// <summary>The start in flight, if any; exposed so tests can await it.</summary>
    internal Task? PendingStart { get; private set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourcePickerVisibility));
            if (!value)
            {
                StopSource();
                RestNow();
            }
            else
            {
                PendingStart = RestartSourceAsync(isUserInitiated: false);
            }

            RefreshStatus();
        }
    }

    public VuMeterSource SelectedSource
    {
        get => _selectedSource;
        set => SelectSource(value);
    }

    /// <summary>
    /// Index into <see cref="AvailableSources"/>, for a ComboBox. Writing the
    /// current index does nothing, so a two-way binding echoing the value back
    /// never restarts the source.
    /// </summary>
    public int SelectedSourceIndex
    {
        get => IndexOf(_selectedSource);
        set
        {
            if (value >= 0 && value < AvailableSources.Count && value != IndexOf(_selectedSource))
            {
                SelectSource(AvailableSources[value]);
            }
        }
    }

    public VuMeterMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeSummary));
            RefreshStatus();
        }
    }

    /// <summary>Says which source is in use and whether it is showing levels.</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>A few words for menus: whether the bars are live right now.</summary>
    public string ModeSummary => _mode == VuMeterMode.Live
        ? $"Showing live {_selectedSource.GetDisplayName().ToLowerInvariant()}"
        : "No live audio: plain seek bar";

    /// <summary>The source picker only matters when the meter is on and the platform has a live source.</summary>
    public Visibility SourcePickerVisibility => _isEnabled && HasLiveSources ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// True while the pointer is over a clickable seek bar. The bars fade to
    /// rest so the plain seek bar reads as a control, and come back when the
    /// pointer leaves.
    /// </summary>
    public bool IsPointerOver => _isPointerOver;

    public void SetPointerOver(bool isOver)
    {
        if (_isPointerOver == isOver)
        {
            return;
        }

        _isPointerOver = isOver;
        OnPropertyChanged(nameof(IsPointerOver));
    }

    /// <summary>
    /// Picks a source from a click. Picking the current source again restarts
    /// it, which is how a browser share is resumed after it was stopped.
    /// </summary>
    public void SelectSource(VuMeterSource source)
    {
        if (IndexOf(source) < 0)
        {
            return;
        }

        bool changed = _selectedSource != source;
        _selectedSource = source;
        _preferences.VuMeterSource = source;
        if (changed)
        {
            OnPropertyChanged(nameof(SelectedSource));
            OnPropertyChanged(nameof(SelectedSourceIndex));
            OnPropertyChanged(nameof(ModeSummary));
        }

        PendingStart = RestartSourceAsync(isUserInitiated: true);
        RefreshStatus();
    }

    /// <summary>Starts the stored source at launch, without any click.</summary>
    public Task ActivateAsync()
    {
        PendingStart = RestartSourceAsync(isUserInitiated: false);
        return PendingStart;
    }

    /// <summary>Linearly interpolated level at a point 0..1 across the bands.</summary>
    public float SampleLevel(double fraction)
    {
        double position = Math.Clamp(fraction, 0, 1) * (BAND_COUNT - 1);
        int lower = (int)position;
        int upper = Math.Min(lower + 1, BAND_COUNT - 1);
        double t = position - lower;
        return (float)(_levels[lower] + (_levels[upper] - _levels[lower]) * t);
    }

    /// <summary>
    /// How visible a bar of the given level is: transparent up to
    /// <see cref="INVISIBLE_LEVEL"/>, easing to opaque at <see cref="OPAQUE_LEVEL"/>.
    /// </summary>
    public static double GetBarOpacity(float level)
    {
        double t = Math.Clamp((level - INVISIBLE_LEVEL) / (OPAQUE_LEVEL - INVISIBLE_LEVEL), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// How tall a bar of the given level is, as a fraction of the meter's
    /// height. The curve lifts mid levels so a moderately loud band still
    /// reads clearly on a thin seek bar.
    /// </summary>
    public static double GetBarHeight(float level) => Math.Sqrt(Math.Clamp(level, 0, 1));

    /// <summary>One frame of the meter.</summary>
    /// <param name="nowMs">A monotonic clock in milliseconds.</param>
    /// <param name="isPlaying">When false the bars fall to rest.</param>
    public void Update(long nowMs, bool isPlaying)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (_lastFrameMs != long.MinValue && nowMs >= _lastFrameMs && nowMs - _lastFrameMs < FRAME_INTERVAL_MS)
        {
            return;
        }

        _lastFrameMs = nowMs;

        bool isLive = ReadLiveSource(nowMs);
        Mode = isLive ? VuMeterMode.Live : VuMeterMode.Static;

        if (isPlaying && isLive && !_isPointerOver)
        {
            Array.Copy(_live, _target, BAND_COUNT);
        }
        else
        {
            Array.Clear(_target);
        }

        Smooth();
    }

    public void Dispose()
    {
        StopSource();
    }

    private bool ReadLiveSource(long nowMs)
    {
        if (_source is null)
        {
            return false;
        }

        if (!_source.IsActive)
        {
            _logger.LogInformation("VU meter source {Source} ended", _selectedSource);
            StopSource();
            _sourceEnded = true;
            RefreshStatus();
            return false;
        }

        if (_source.TryReadBands(_live))
        {
            _lastLiveDataMs = nowMs;
            if (Peak(_live) >= SILENCE_LEVEL)
            {
                _lastSoundMs = nowMs;
            }
        }

        bool isFresh = _lastLiveDataMs != long.MinValue && nowMs - _lastLiveDataMs < STALL_MS;
        bool heardRecently = _lastSoundMs != long.MinValue && nowMs - _lastSoundMs < SILENCE_FALLBACK_MS;
        return isFresh && heardRecently;
    }

    private void Smooth()
    {
        bool atRest = true;
        for (int i = 0; i < BAND_COUNT; i++)
        {
            float current = _levels[i];
            float target = _target[i];
            float rate = target > current ? ATTACK : RELEASE;
            float next = current + (target - current) * rate;
            if (next < REST_LEVEL && target < REST_LEVEL)
            {
                next = 0;
            }

            _levels[i] = next;
            atRest &= next == 0;
        }

        if (atRest && _isAtRest)
        {
            return;
        }

        _isAtRest = atRest;
        LevelsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RestNow()
    {
        Array.Clear(_levels);
        Array.Clear(_target);
        if (!_isAtRest)
        {
            _isAtRest = true;
            LevelsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RestartSourceAsync(bool isUserInitiated)
    {
        StopSource();
        _startResult = null;
        _sourceEnded = false;

        if (!_isEnabled || _selectedSource == VuMeterSource.None)
        {
            return;
        }

        VuMeterSource requested = _selectedSource;
        IAudioLevelSource? source = _provider.Create(requested);
        if (source is null)
        {
            _startResult = AudioLevelSourceStartResult.Unavailable;
            return;
        }

        CancellationTokenSource cts = new();
        _startCts = cts;

        AudioLevelSourceStartResult result;
        try
        {
            result = await source.StartAsync(isUserInitiated, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VU meter source {Source} failed to start", requested);
            result = AudioLevelSourceStartResult.Unavailable;
        }
        catch (OperationCanceledException)
        {
            source.Dispose();
            return;
        }

        if (cts.IsCancellationRequested)
        {
            source.Dispose();
            return;
        }

        _startCts = null;
        cts.Dispose();
        _startResult = result;
        if (result == AudioLevelSourceStartResult.Started)
        {
            _source = source;
            _lastLiveDataMs = long.MinValue;
            _lastSoundMs = long.MinValue;
        }
        else
        {
            _logger.LogInformation("VU meter source {Source} did not start: {Result}", requested, result);
            source.Dispose();
        }

        RefreshStatus();
    }

    private void StopSource()
    {
        if (_startCts is not null)
        {
            _startCts.Cancel();
            _startCts = null;
        }

        _source?.Dispose();
        _source = null;
        Mode = VuMeterMode.Static;
    }

    private void RefreshStatus() => StatusText = BuildStatusText();

    private string BuildStatusText()
    {
        if (!_isEnabled)
        {
            return "Off.";
        }

        string name = _selectedSource.GetDisplayName();
        if (_selectedSource == VuMeterSource.None)
        {
            return HasLiveSources
                ? "No audio source picked; the seek bar stays plain."
                : "No audio source on this device; the seek bar stays plain.";
        }

        if (_mode == VuMeterMode.Live)
        {
            return $"Live: levels from {name.ToLowerInvariant()}.";
        }

        if (_sourceEnded)
        {
            return $"{name} stopped. Pick it again to resume.";
        }

        return _startResult switch
        {
            AudioLevelSourceStartResult.Denied => $"{name} access was denied; the seek bar stays plain.",
            AudioLevelSourceStartResult.Unavailable => $"{name} is not available on this device; the seek bar stays plain.",
            AudioLevelSourceStartResult.NeedsUserAction => $"Pick {name.ToLowerInvariant()} again to share audio.",
            AudioLevelSourceStartResult.Started => $"Waiting for sound from {name.ToLowerInvariant()}.",
            _ => $"Starting {name.ToLowerInvariant()}...",
        };
    }

    private int IndexOf(VuMeterSource source)
    {
        for (int i = 0; i < AvailableSources.Count; i++)
        {
            if (AvailableSources[i] == source)
            {
                return i;
            }
        }

        return -1;
    }

    private static float Peak(ReadOnlySpan<float> values)
    {
        float peak = 0;
        foreach (float value in values)
        {
            peak = Math.Max(peak, value);
        }

        return peak;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
