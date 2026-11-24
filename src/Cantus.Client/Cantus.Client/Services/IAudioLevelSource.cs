using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;

namespace Cantus.Client.Services;

public enum AudioLevelSourceStartResult
{
    Started,

    /// <summary>The user or the browser refused access.</summary>
    Denied,

    /// <summary>This device cannot provide the source (missing tool, no device, unsupported browser).</summary>
    Unavailable,

    /// <summary>The source can only start from a click, such as a browser share picker.</summary>
    NeedsUserAction,
}

/// <summary>A live audio feed analysed into meter bands.</summary>
public interface IAudioLevelSource : IDisposable
{
    /// <param name="isUserInitiated">True when started from a click, which browser pickers require.</param>
    Task<AudioLevelSourceStartResult> StartAsync(bool isUserInitiated, CancellationToken cancellationToken);

    /// <summary>False once the stream ended, for example when the user stops sharing.</summary>
    bool IsActive { get; }

    /// <summary>Fills levels 0..1 per band. Returns false when no new audio arrived since the last read.</summary>
    bool TryReadBands(Span<float> bands);
}

/// <summary>The live sources this platform offers.</summary>
public interface IAudioLevelSourceProvider
{
    /// <summary>Always starts with <see cref="VuMeterSource.None"/>.</summary>
    IReadOnlyList<VuMeterSource> AvailableSources { get; }

    /// <summary>The source to use before the user picks one.</summary>
    VuMeterSource DefaultSource { get; }

    IAudioLevelSource? Create(VuMeterSource source);
}

public sealed class NoAudioLevelSourceProvider : IAudioLevelSourceProvider
{
    public static NoAudioLevelSourceProvider Instance { get; } = new();

    public IReadOnlyList<VuMeterSource> AvailableSources { get; } = [VuMeterSource.None];

    public VuMeterSource DefaultSource => VuMeterSource.None;

    public IAudioLevelSource? Create(VuMeterSource source) => null;
}
