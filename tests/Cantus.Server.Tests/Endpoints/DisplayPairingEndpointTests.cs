using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// The pairing flow end to end: a waiting client on a real hub connection requests a code,
/// an owner approves it over REST, and the Display token arrives on that connection.
/// </summary>
public sealed class DisplayPairingEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string DISPLAY_NAME = "Living room TV";
    private const int CODE_LIFETIME_SECONDS = 60;
    private const int RATE_LIMIT_PERMITS = 3;

    private readonly DisplayTestHost _host;

    public DisplayPairingEndpointTests(WebApplicationFactory<Program> factory)
    {
        _host = new DisplayTestHost(factory, options =>
        {
            options.CodeLifetimeSeconds = CODE_LIFETIME_SECONDS;
            options.RateLimitPermitLimit = RATE_LIMIT_PERMITS;
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task RequestPairing_ForLiveConnection_ReturnsShortCodeAndPairingId()
    {
        HubConnection waiting = await _host.ConnectAsync();

        DisplayPairingDto pairing = await _host.RequestPairingAsync(waiting);

        pairing.PairingId.Should().NotBeNullOrWhiteSpace();
        pairing.Code.Should().HaveLength(new DisplayPairingOptions().CodeLength);
        pairing.ExpiresAtUtc.Should().Be(_host.Time.GetUtcNow().AddSeconds(CODE_LIFETIME_SECONDS));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-connection")]
    public async Task RequestPairing_WithoutLiveConnection_ReturnsBadRequest(string? connectionId)
    {
        HttpResponseMessage response = await _host.CreateClient().PostAsJsonAsync(
            DisplayTestHost.PAIRINGS_PATH,
            new DisplayPairingRequestDto { ConnectionId = connectionId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestPairing_OverRateLimit_ReturnsTooManyRequests()
    {
        HubConnection waiting = await _host.ConnectAsync();
        HttpClient client = _host.CreateClient();
        DisplayPairingRequestDto request = new() { ConnectionId = waiting.ConnectionId };

        for (int i = 0; i < RATE_LIMIT_PERMITS; i++)
        {
            (await client.PostAsJsonAsync(DisplayTestHost.PAIRINGS_PATH, request)).EnsureSuccessStatusCode();
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync(DisplayTestHost.PAIRINGS_PATH, request);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RequestPairing_WithPathCaseAndTrailingSlashVariants_SharesOneRateLimit()
    {
        HubConnection waiting = await _host.ConnectAsync();
        HttpClient client = _host.CreateClient();
        DisplayPairingRequestDto request = new() { ConnectionId = waiting.ConnectionId };
        string[] variants =
        {
            "/API/displays/pairings",
            "/api/Displays/Pairings/",
            "/Api/DISPLAYS/pairings",
        };

        foreach (string path in variants)
        {
            (await client.PostAsJsonAsync(path, request)).EnsureSuccessStatusCode();
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/displays/PAIRINGS/", request);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Approve_WithPathCaseVariants_SharesOneRateLimit()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        HttpClient client = _host.CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id);
        DisplayPairingApprovalDto approval = new() { Code = "UNKNOWN1", Name = DISPLAY_NAME };
        string[] variants =
        {
            "/API/displays/pairings/approve",
            "/api/Displays/Pairings/Approve/",
            "/Api/DISPLAYS/pairings/APPROVE",
        };

        foreach (string path in variants)
        {
            (await client.PostAsJsonAsync(path, approval)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/displays/pairings/approve/", approval);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RequestPairingAndApprove_HaveSeparateRateLimits()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        HubConnection waiting = await _host.ConnectAsync();
        HttpClient client = _host.CreateClient();
        DisplayPairingRequestDto request = new() { ConnectionId = waiting.ConnectionId };

        for (int i = 0; i < RATE_LIMIT_PERMITS; i++)
        {
            (await client.PostAsJsonAsync(DisplayTestHost.PAIRINGS_PATH, request)).EnsureSuccessStatusCode();
        }

        HttpResponseMessage approval = await _host.ApproveAsync(
            "UNKNOWN1",
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        approval.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Approve_OverRateLimit_ReturnsTooManyRequests()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");

        for (int i = 0; i < RATE_LIMIT_PERMITS; i++)
        {
            HttpResponseMessage response = await _host.ApproveAsync(
                "UNKNOWN1",
                DISPLAY_NAME,
                DisplayTestHost.SESSION_COOKIE,
                owner.Id);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        HttpResponseMessage limited = await _host.ApproveAsync(
            "UNKNOWN1",
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Approve_WithSession_DeliversWorkingDisplayTokenToWaitingConnection()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        HubConnection waiting = await _host.ConnectAsync();
        TaskCompletionSource<DisplayPairedDto> paired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.On<DisplayPairedDto>(nameof(IPlaybackClient.ReceiveDisplayPaired), dto => paired.TrySetResult(dto));
        DisplayPairingDto pairing = await _host.RequestPairingAsync(waiting);

        HttpResponseMessage response = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        DisplayDto display = (await response.Content.ReadFromJsonAsync<DisplayDto>())!;
        display.Name.Should().Be(DISPLAY_NAME);
        response.Headers.Location!.ToString().Should().Be($"{DisplayTestHost.DISPLAYS_PATH}/{display.Id}");

        DisplayPairedDto delivered = await DisplayTestHost.WaitAsync(paired.Task);
        delivered.PairingId.Should().Be(pairing.PairingId);
        delivered.DisplayId.Should().Be(display.Id);
        delivered.Name.Should().Be(DISPLAY_NAME);

        DisplayInfo? validated = await _host.Store.CreateDisplayService().ValidateAsync(delivered.Token);
        validated!.Id.Should().Be(display.Id);
        validated.OwnerSessionId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Approve_SameCodeTwice_SecondReturnsNotFound()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        HttpResponseMessage first = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);
        HttpResponseMessage second = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Approve_WithDisplayToken_ReturnsForbiddenAndKeepsCode()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay existing = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "Kitchen");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        HttpResponseMessage rejected = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.DISPLAY_COOKIE,
            existing.Token);

        rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id)).Should().ContainSingle();
        await AssertCodeStillApprovableAsync(pairing.Code, owner.Id);
    }

    [Fact]
    public async Task Approve_WithDisplayTokenAsBearer_ReturnsForbidden()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay existing = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "Kitchen");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());
        HttpClient client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", existing.Token);

        HttpResponseMessage rejected = await client.PostAsJsonAsync(
            DisplayTestHost.APPROVE_PATH,
            new DisplayPairingApprovalDto { Code = pairing.Code, Name = DISPLAY_NAME });

        rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approve_WithoutCredential_ReturnsUnauthorizedAndKeepsCode()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        HttpResponseMessage rejected = await _host.ApproveAsync(pairing.Code, DISPLAY_NAME, null, null);

        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertCodeStillApprovableAsync(pairing.Code, owner.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Approve_WithInvalidName_ReturnsBadRequestAndKeepsCode(string? name)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        HttpResponseMessage rejected = await _host.ApproveAsync(
            pairing.Code,
            name!,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertCodeStillApprovableAsync(pairing.Code, owner.Id);
    }

    [Fact]
    public async Task Approve_WithTooLongName_ReturnsBadRequest()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        HttpResponseMessage rejected = await _host.ApproveAsync(
            pairing.Code,
            new string('x', DisplayInfo.MAX_NAME_LENGTH + 1),
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_AfterCodeExpires_ReturnsNotFound()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        DisplayPairingDto pairing = await _host.RequestPairingAsync(await _host.ConnectAsync());

        _host.Time.Advance(TimeSpan.FromSeconds(CODE_LIFETIME_SECONDS));
        HttpResponseMessage response = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_AfterWaitingConnectionDisconnects_ReturnsNotFound()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        HubConnection waiting = await _host.ConnectAsync();
        string connectionId = waiting.ConnectionId!;
        DisplayPairingDto pairing = await _host.RequestPairingAsync(waiting);

        await waiting.StopAsync();
        await _host.WaitForServerDisconnectAsync(connectionId);
        HttpResponseMessage response = await _host.ApproveAsync(
            pairing.Code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            owner.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task AssertCodeStillApprovableAsync(string code, string ownerSessionId)
    {
        HttpResponseMessage approved = await _host.ApproveAsync(
            code,
            DISPLAY_NAME,
            DisplayTestHost.SESSION_COOKIE,
            ownerSessionId);
        approved.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
