#if !__WASM__
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services.Audio;

/// <summary>
/// Linux system audio: reads the monitor of the default output through
/// PulseAudio's <c>parec</c> (also served by PipeWire's pulse layer), or
/// <c>pw-record</c> on PipeWire without it, as raw mono float PCM on stdout.
/// The capture hears everything the default output plays, not just the player,
/// and stays on the output that was default when it started.
/// </summary>
internal sealed class MonitorCaptureLevelSource : IAudioLevelSource
{
    private const int SAMPLE_RATE = 44100;

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<MonitorCaptureLevelSource>();

    private readonly SpectrumAnalyzer _analyzer = new(SAMPLE_RATE);
    private readonly CancellationTokenSource _cts = new();
    private Process? _process;
    private volatile bool _isActive;
    private volatile bool _isDisposed;

    public bool IsActive => _isActive;

    /// <summary>True when a capture tool is on the PATH; checked once when the source list is built.</summary>
    public static bool IsSupported() =>
        OperatingSystem.IsLinux() && (FindOnPath("parec") is not null || FindOnPath("pw-record") is not null);

    public async Task<AudioLevelSourceStartResult> StartAsync(bool isUserInitiated, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return AudioLevelSourceStartResult.Unavailable;
        }

        foreach (ProcessStartInfo candidate in Candidates())
        {
            Process? process = TryStart(candidate);
            if (process is null)
            {
                continue;
            }

            // A missing monitor or server makes the tool exit at once; give it a moment to fail.
            await Task.Delay(250, cancellationToken);
            if (process.HasExited)
            {
                _logger.LogInformation("{Tool} exited with code {Code}", candidate.FileName, process.ExitCode);
                process.Dispose();
                continue;
            }

            _process = process;
            _isActive = true;
            _ = Task.Run(() => ReadLoopAsync(process, _cts.Token), CancellationToken.None);
            return AudioLevelSourceStartResult.Started;
        }

        return AudioLevelSourceStartResult.Unavailable;
    }

    public bool TryReadBands(Span<float> bands) => _analyzer.TryAnalyze(bands);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isActive = false;
        _cts.Cancel();
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            _process.Dispose();
        }

        _cts.Dispose();
    }

    private static IEnumerable<ProcessStartInfo> Candidates()
    {
        ProcessStartInfo parec = Redirected("parec");
        parec.ArgumentList.Add("--raw");
        parec.ArgumentList.Add("--format=float32le");
        parec.ArgumentList.Add($"--rate={SAMPLE_RATE}");
        parec.ArgumentList.Add("--channels=1");
        parec.ArgumentList.Add("--latency-msec=20");
        parec.ArgumentList.Add("--device=@DEFAULT_MONITOR@");
        parec.ArgumentList.Add("--client-name=Cantus VU meter");
        yield return parec;

        ProcessStartInfo pwRecord = Redirected("pw-record");
        pwRecord.ArgumentList.Add("--raw");
        pwRecord.ArgumentList.Add("--format=f32");
        pwRecord.ArgumentList.Add($"--rate={SAMPLE_RATE}");
        pwRecord.ArgumentList.Add("--channels=1");
        pwRecord.ArgumentList.Add("--latency=20ms");
        pwRecord.ArgumentList.Add("-P");
        pwRecord.ArgumentList.Add("{ stream.capture.sink=true node.name=cantus-vu-meter }");
        pwRecord.ArgumentList.Add("-");
        yield return pwRecord;
    }

    private static ProcessStartInfo Redirected(string fileName) => new(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    private static Process? TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process? process = Process.Start(startInfo);
            if (process is not null)
            {
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _logger.LogDebug("{Tool}: {Line}", startInfo.FileName, e.Data);
                    }
                };
                process.BeginErrorReadLine();
            }

            return process;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        int carried = 0;
        try
        {
            Stream stdout = process.StandardOutput.BaseStream;
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await stdout.ReadAsync(buffer.AsMemory(carried), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                int available = carried + read;
                int whole = available - (available % sizeof(float));
                _analyzer.Push(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, whole)));
                carried = available - whole;
                buffer.AsSpan(whole, carried).CopyTo(buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
        finally
        {
            if (!_isDisposed)
            {
                _logger.LogInformation("System audio capture ended");
            }

            _isActive = false;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
#endif
