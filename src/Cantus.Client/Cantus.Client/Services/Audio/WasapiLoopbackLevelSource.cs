#if !__WASM__
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Cantus.Client.Services.Audio;

/// <summary>
/// Windows system audio through WASAPI loopback on the default output. WASAPI
/// sends no packets while nothing plays, which the meter reads as a stall and
/// answers with the decorative pattern. The capture stays on the output that
/// was default when it started.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiLoopbackLevelSource : IAudioLevelSource
{
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<WasapiLoopbackLevelSource>();

    private WasapiLoopbackCapture? _capture;
    private SpectrumAnalyzer? _analyzer;
    private float[] _mono = [];
    private int _channels;
    private bool _isFloat;
    private volatile bool _isActive;

    public bool IsActive => _isActive;

    public Task<AudioLevelSourceStartResult> StartAsync(bool isUserInitiated, CancellationToken cancellationToken)
    {
        try
        {
            WasapiLoopbackCapture capture = new();
            WaveFormat format = capture.WaveFormat;
            _isFloat = format.BitsPerSample == 32;
            if (!_isFloat && format.BitsPerSample != 16)
            {
                _logger.LogInformation("Unsupported loopback format: {Format}", format);
                capture.Dispose();
                return Task.FromResult(AudioLevelSourceStartResult.Unavailable);
            }

            _channels = Math.Max(1, format.Channels);
            _analyzer = new SpectrumAnalyzer(format.SampleRate);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            _isActive = true;
            capture.StartRecording();
            return Task.FromResult(AudioLevelSourceStartResult.Started);
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "WASAPI loopback capture failed to start");
            _isActive = false;
            return Task.FromResult(AudioLevelSourceStartResult.Unavailable);
        }
    }

    public bool TryReadBands(Span<float> bands) => _analyzer?.TryAnalyze(bands) ?? false;

    public void Dispose()
    {
        _isActive = false;
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        try
        {
            _capture.StopRecording();
        }
        catch (COMException)
        {
        }

        _capture.Dispose();
        _capture = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _isFloat ? sizeof(float) : sizeof(short);
        int frames = e.BytesRecorded / (bytesPerSample * _channels);
        if (frames == 0 || _analyzer is null)
        {
            return;
        }

        if (_mono.Length < frames)
        {
            _mono = new float[frames];
        }

        ReadOnlySpan<byte> bytes = e.Buffer.AsSpan(0, frames * bytesPerSample * _channels);
        if (_isFloat)
        {
            DownmixFloat(MemoryMarshal.Cast<byte, float>(bytes), _mono.AsSpan(0, frames));
        }
        else
        {
            DownmixPcm16(MemoryMarshal.Cast<byte, short>(bytes), _mono.AsSpan(0, frames));
        }

        _analyzer.Push(_mono.AsSpan(0, frames));
    }

    private void DownmixFloat(ReadOnlySpan<float> interleaved, Span<float> mono)
    {
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0;
            for (int channel = 0; channel < _channels; channel++)
            {
                sum += interleaved[frame * _channels + channel];
            }

            mono[frame] = sum / _channels;
        }
    }

    private void DownmixPcm16(ReadOnlySpan<short> interleaved, Span<float> mono)
    {
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0;
            for (int channel = 0; channel < _channels; channel++)
            {
                sum += interleaved[frame * _channels + channel] / 32768f;
            }

            mono[frame] = sum / _channels;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogInformation(e.Exception, "WASAPI loopback capture stopped");
        }

        _isActive = false;
    }
}
#endif
