#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.ViewModels;

namespace Cantus.Client.Services.Audio;

/// <summary>
/// Browser audio through the Web Audio AnalyserNode (window.CantusAudio in
/// index.html). The microphone hears the room, so a display reacts to speakers
/// playing Spotify anywhere nearby. Shared audio comes from the browser's share
/// picker; it needs a click every time and only Chromium browsers include audio.
/// Both need a secure context (HTTPS or localhost).
/// </summary>
internal sealed partial class BrowserAudioLevelSource : IAudioLevelSource
{
    private const int BIN_COUNT = 1024;

    private static int _nextSessionId;

    private readonly VuMeterSource _source;
    private readonly int _sessionId = Interlocked.Increment(ref _nextSessionId);
    private readonly byte[] _bins = new byte[BIN_COUNT];
    private readonly float[] _binLevels = new float[BIN_COUNT];
    private bool _isActive;

    public BrowserAudioLevelSource(VuMeterSource source)
    {
        _source = source;
    }

    public bool IsActive => _isActive;

    public static bool IsSupported(VuMeterSource source) => source switch
    {
        VuMeterSource.Microphone => Supports("microphone"),
        VuMeterSource.SharedAudio => Supports("display"),
        _ => false,
    };

    public async Task<AudioLevelSourceStartResult> StartAsync(bool isUserInitiated, CancellationToken cancellationToken)
    {
        if (_source == VuMeterSource.SharedAudio && !isUserInitiated)
        {
            return AudioLevelSourceStartResult.NeedsUserAction;
        }

        string result = await Start(_source == VuMeterSource.SharedAudio ? "display" : "microphone", _sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        switch (result)
        {
            case "ok":
                _isActive = true;
                return AudioLevelSourceStartResult.Started;
            case "denied":
                return AudioLevelSourceStartResult.Denied;
            case "no-audio":
                // A window or screen shared without its audio ticked.
                return AudioLevelSourceStartResult.NeedsUserAction;
            default:
                return AudioLevelSourceStartResult.Unavailable;
        }
    }

    public bool TryReadBands(Span<float> bands)
    {
        if (!_isActive)
        {
            return false;
        }

        int sampleRate = ReadBins(_sessionId, _bins);
        if (sampleRate < 0)
        {
            _isActive = false;
            return false;
        }

        if (sampleRate == 0)
        {
            return false;
        }

        for (int i = 0; i < BIN_COUNT; i++)
        {
            _binLevels[i] = _bins[i] / 255f;
        }

        SpectrumAnalyzer.MapBins(_binLevels, sampleRate, bands);
        return true;
    }

    public void Dispose()
    {
        _isActive = false;
        Stop(_sessionId);
    }

    [JSImport("globalThis.CantusAudio.supports")]
    private static partial bool Supports(string kind);

    [JSImport("globalThis.CantusAudio.start")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Start(string kind, int sessionId);

    /// <summary>
    /// Copies the newest byte spectrum. Returns the sample rate, 0 when not
    /// ready, or -1 once this session's stream ended or a newer one replaced it.
    /// </summary>
    [JSImport("globalThis.CantusAudio.readBins")]
    private static partial int ReadBins(int sessionId, [JSMarshalAs<JSType.MemoryView>] Span<byte> bins);

    /// <summary>Stops the stream only if it still belongs to this session.</summary>
    [JSImport("globalThis.CantusAudio.stop")]
    private static partial void Stop(int sessionId);
}
#endif
