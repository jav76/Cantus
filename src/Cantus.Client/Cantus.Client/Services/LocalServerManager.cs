using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public enum LocalServerStartResult
{
    /// <summary>This build ships no server, or the address is not this computer's default one.</summary>
    NotManaged,

    /// <summary>A Cantus server was already answering and was left alone.</summary>
    AlreadyRunning,

    /// <summary>The bundled server was started and answers.</summary>
    Started,

    /// <summary>The bundled server was started but did not answer in time, or could not be started.</summary>
    Failed,
}

public interface ILocalServer
{
    /// <summary>True when this app ships a server and would run it for the given address.</summary>
    bool Manages(string baseUrl);

    Task<LocalServerStartResult> EnsureRunningAsync(string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>Stops the server this app started. False when none answers its control token.</summary>
    Task<bool> StopAsync(string baseUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the server shipped with the desktop app as a separate, per-user process that keeps
/// running after the app closes. A server that already answers is reused, unless this app started
/// it from an older version, in which case it is replaced.
/// </summary>
public sealed class LocalServerManager : ILocalServer
{
    public static readonly TimeSpan START_TIMEOUT = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan STOP_TIMEOUT = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromMilliseconds(250);

    private const string LOCALHOST_BASE_URL = "http://localhost:5000";
    private const int CONTROL_TOKEN_BYTES = 32;

    private static readonly Lazy<LocalServerManager> _forThisApp = new(CreateForThisApp);

    private readonly LocalServerBundle? _bundle;
    private readonly ILocalServerApi _api;
    private readonly ILocalServerProcess _process;
    private readonly ILocalServerStateStore? _state;
    private readonly string _appVersion;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Task<LocalServerStartResult> _startup = Task.FromResult(LocalServerStartResult.NotManaged);

    public LocalServerManager(
        LocalServerBundle? bundle,
        ILocalServerApi api,
        ILocalServerProcess process,
        ILocalServerStateStore? state,
        string appVersion,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger? logger = null)
    {
        _bundle = bundle;
        _api = api;
        _process = process;
        _state = state;
        _appVersion = appVersion;
        _delay = delay ?? Task.Delay;
        _logger = logger ?? ClientLoggingManager.CreateLogger(nameof(LocalServerManager));
    }

    /// <summary>The manager for the server shipped next to this app, if any.</summary>
    public static LocalServerManager ForThisApp => _forThisApp.Value;

    public bool IsBundled => _bundle is not null && _state is not null;

    public bool Manages(string baseUrl) =>
        IsBundled
        && (ServerAddress.AreSame(baseUrl, ServerAddress.DEFAULT_BASE_URL) || ServerAddress.AreSame(baseUrl, LOCALHOST_BASE_URL));

    /// <summary>
    /// Starts the server in the background at app launch. <see cref="WhenStartedAsync"/> lets the
    /// first connection wait for it instead of failing and retrying.
    /// </summary>
    public void BeginStartup(string baseUrl)
    {
        if (Manages(baseUrl))
        {
            _startup = EnsureRunningAsync(baseUrl);
        }
    }

    /// <summary>Completes when the launch started by <see cref="BeginStartup"/> has settled. Never throws.</summary>
    public async Task WhenStartedAsync()
    {
        try
        {
            await _startup;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Starting the server on this computer failed");
        }
    }

    public async Task<LocalServerStartResult> EnsureRunningAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        if (!Manages(baseUrl))
        {
            return LocalServerStartResult.NotManaged;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? runningVersion = await _api.GetVersionAsync(baseUrl, cancellationToken);
            if (runningVersion is not null && !await ReplaceOutdatedAsync(baseUrl, runningVersion, cancellationToken))
            {
                return LocalServerStartResult.AlreadyRunning;
            }

            return await StartAsync(baseUrl, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        if (!Manages(baseUrl))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await StopOwnedAsync(baseUrl, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Only a server this app started is replaced: one the user runs themselves is theirs to upgrade.
    private async Task<bool> ReplaceOutdatedAsync(string baseUrl, string runningVersion, CancellationToken cancellationToken)
    {
        if (string.Equals(runningVersion, _appVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool isStopped = await StopOwnedAsync(baseUrl, cancellationToken);
        if (isStopped)
        {
            _logger.LogInformation(
                "Replaced the server on this computer: it ran {RunningVersion}, this app is {AppVersion}",
                runningVersion,
                _appVersion);
        }

        return isStopped;
    }

    private async Task<bool> StopOwnedAsync(string baseUrl, CancellationToken cancellationToken)
    {
        string? controlToken = _state!.ReadControlToken();
        if (controlToken is null || !await _api.RequestShutdownAsync(baseUrl, controlToken, cancellationToken))
        {
            return false;
        }

        bool isStopped = await PollAsync(
            async () => await _api.GetVersionAsync(baseUrl, cancellationToken) is null,
            STOP_TIMEOUT,
            cancellationToken);
        if (isStopped)
        {
            _state.Clear();
        }

        return isStopped;
    }

    private async Task<LocalServerStartResult> StartAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (!_process.IsAddressFree(ServerAddress.DEFAULT_BASE_URL))
        {
            _logger.LogError(
                "Another program is using {BaseUrl}, so the server on this computer cannot start",
                ServerAddress.DEFAULT_BASE_URL);

            return LocalServerStartResult.Failed;
        }

        string controlToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(CONTROL_TOKEN_BYTES));
        try
        {
            _state!.SaveControlToken(controlToken);
            _process.StartDetached(new LocalServerLaunch(_bundle!, ServerAddress.DEFAULT_BASE_URL, controlToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the server on this computer");

            return LocalServerStartResult.Failed;
        }

        bool isAnswering = await PollAsync(
            async () => await _api.GetVersionAsync(baseUrl, cancellationToken) is not null,
            START_TIMEOUT,
            cancellationToken);
        if (!isAnswering)
        {
            _logger.LogError("The server on this computer did not answer within {Seconds} seconds", START_TIMEOUT.TotalSeconds);
        }

        return isAnswering ? LocalServerStartResult.Started : LocalServerStartResult.Failed;
    }

    private async Task<bool> PollAsync(Func<Task<bool>> isDone, TimeSpan timeout, CancellationToken cancellationToken)
    {
        for (TimeSpan waited = TimeSpan.Zero; waited < timeout; waited += POLL_INTERVAL)
        {
            if (await isDone())
            {
                return true;
            }

            await _delay(POLL_INTERVAL, cancellationToken);
        }

        return await isDone();
    }

    private static LocalServerManager CreateForThisApp()
    {
        LocalServerBundle? bundle = LocalServerBundle.FindForThisApp();

        return new LocalServerManager(
            bundle,
            new LocalServerApi(new HttpClient()),
            new LocalServerProcess(),
            bundle is null ? null : new LocalServerStateStore(bundle.DataDirectory),
            BuildInfo.SemVer);
    }
}
