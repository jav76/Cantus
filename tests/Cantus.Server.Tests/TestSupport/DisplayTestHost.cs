using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Cantus.Server.Tests.TestSupport;

/// <summary>
/// A full server over TestServer with real auth, Display and pairing services, a fake clock,
/// and real SignalR client connections. The playback poller is removed so no test reaches
/// Spotify.
/// </summary>
public sealed class DisplayTestHost : IAsyncDisposable
{
    public const string SESSION_COOKIE = "cantus_session_id";
    public const string DISPLAY_COOKIE = "cantus_display_token";
    public const string HUB_PATH = "hubs/playback";
    public const string PAIRINGS_PATH = "/api/displays/pairings";
    public const string APPROVE_PATH = "/api/displays/pairings/approve";
    public const string DISPLAYS_PATH = "/api/displays";

    private static readonly TimeSpan WAIT_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromMilliseconds(20);

    private readonly List<HubConnection> _connections = new();

    public DisplayTestHost(
        WebApplicationFactory<Program> factory,
        Action<DisplayPairingOptions>? configurePairing = null)
    {
        Factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ServiceDescriptor? poller = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(ActiveUsersPlaybackMonitor));
                if (poller is not null)
                {
                    services.Remove(poller);
                }

                services.AddSingleton<TimeProvider>(Time);
                services.AddScoped<ISpotifyAuthService>(_ => Store.CreateAuthService());
                services.AddScoped<IDisplayService>(_ => Store.CreateDisplayService());
                services.AddScoped(_ => Mock.Of<ISpotifyPlayerClient>());
                services.AddScoped(_ => Mock.Of<ILyricsCacheRepository>());
                if (configurePairing is not null)
                {
                    services.Configure(configurePairing);
                }
            });
        });
    }

    public InMemorySessionStore Store { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient CreateClient(string? cookieName = null, string? cookieValue = null)
    {
        HttpClient client = Factory.CreateClient();
        if (cookieName is not null && cookieValue is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={cookieValue}");
        }

        return client;
    }

    /// <summary>
    /// Opens a hub connection and waits until the server has finished resolving its credential,
    /// so the test does not race the server's connect handling.
    /// </summary>
    public async Task<HubConnection> ConnectAsync(string? accessToken = null)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, HUB_PATH), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();

        _connections.Add(connection);
        await connection.StartAsync();

        IPlaybackSessionRegistry registry = Factory.Services.GetRequiredService<IPlaybackSessionRegistry>();
        await WaitUntilAsync(() => registry.IsConnected(connection.ConnectionId!));
        return connection;
    }

    /// <summary>Opens a hub connection with a Display token and waits until it is tracked.</summary>
    public async Task<HubConnection> ConnectDisplayAsync(CreatedDisplay display)
    {
        HubConnection connection = await ConnectAsync(display.Token);

        IDisplayConnectionTracker tracker = Factory.Services.GetRequiredService<IDisplayConnectionTracker>();
        await WaitUntilAsync(() => tracker.GetConnectionIds(display.Display.Id).Contains(connection.ConnectionId));
        return connection;
    }

    public async Task<DisplayPairingDto> RequestPairingAsync(HubConnection connection)
    {
        HttpResponseMessage response = await CreateClient().PostAsJsonAsync(
            PAIRINGS_PATH,
            new DisplayPairingRequestDto { ConnectionId = connection.ConnectionId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DisplayPairingDto>())!;
    }

    public Task<HttpResponseMessage> ApproveAsync(string code, string name, string? cookieName, string? cookieValue)
    {
        return CreateClient(cookieName, cookieValue).PostAsJsonAsync(
            APPROVE_PATH,
            new DisplayPairingApprovalDto { Code = code, Name = name });
    }

    /// <summary>Waits until the server has processed a connection's disconnect.</summary>
    public async Task WaitForServerDisconnectAsync(string connectionId)
    {
        IPlaybackSessionRegistry registry = Factory.Services.GetRequiredService<IPlaybackSessionRegistry>();
        await WaitUntilAsync(() => !registry.IsConnected(connectionId));
    }

    public static Task<T> WaitAsync<T>(Task<T> task) => task.WaitAsync(WAIT_TIMEOUT);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(WAIT_TIMEOUT);
        while (!condition())
        {
            await Task.Delay(POLL_INTERVAL, timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (HubConnection connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await Factory.DisposeAsync();
        Store.Dispose();
    }
}
