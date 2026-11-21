using System.Net;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Hubs;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// Ending a Session (logout or Session revoke) deletes the owner's Displays, so every live
/// connection of those Displays is told it was revoked and closed. Other owners' Displays stay.
/// </summary>
public sealed class SessionEndDisplayEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string LOGOUT = "POST /api/auth/logout";
    private const string REVOKE_SESSION = "DELETE /api/auth/sessions/{userId}";

    private readonly DisplayTestHost _host;

    public SessionEndDisplayEndpointTests(WebApplicationFactory<Program> factory)
    {
        _host = new DisplayTestHost(factory);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    public static TheoryData<string> Endpoints() => new() { LOGOUT, REVOKE_SESSION };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EndSession_NotifiesAndClosesEveryConnectionOfOwnersDisplays(string endpoint)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        CreatedDisplay kitchen = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "Kitchen");
        List<WatchedConnection> watched = new()
        {
            await WatchAsync(tv),
            await WatchAsync(tv),
            await WatchAsync(kitchen),
        };

        HttpResponseMessage response = await _host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id)
            .SendAsync(CreateRequest(endpoint, owner.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (WatchedConnection connection in watched)
        {
            (await DisplayTestHost.WaitAsync(connection.RevokedNotice)).Should().Be(connection.DisplayId);
            await DisplayTestHost.WaitAsync(connection.Closed);
            connection.Connection.State.Should().Be(HubConnectionState.Disconnected);
        }

        (await _host.Store.SessionExistsAsync(owner.Id)).Should().BeFalse();
        (await _host.Store.CreateDisplayService().ValidateAsync(tv.Token)).Should().BeNull();
        (await _host.Store.CreateDisplayService().ValidateAsync(kitchen.Token)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EndSession_LeavesOtherOwnersDisplaysConnected(string endpoint)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        UserSessionEntity otherOwner = await _host.Store.SeedSessionAsync("Other");
        CreatedDisplay ownersDisplay = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        CreatedDisplay othersDisplay = await _host.Store.CreateDisplayService().CreateAsync(otherOwner.Id, "TV");
        WatchedConnection closing = await WatchAsync(ownersDisplay);
        WatchedConnection kept = await WatchAsync(othersDisplay);

        await _host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id)
            .SendAsync(CreateRequest(endpoint, owner.Id));

        await DisplayTestHost.WaitAsync(closing.Closed);
        kept.Connection.State.Should().Be(HubConnectionState.Connected);
        kept.RevokedNotice.IsCompleted.Should().BeFalse();
        (await _host.Store.SessionExistsAsync(otherOwner.Id)).Should().BeTrue();
        (await _host.Store.CreateDisplayService().ValidateAsync(othersDisplay.Token)).Should().NotBeNull();
    }

    private async Task<WatchedConnection> WatchAsync(CreatedDisplay display)
    {
        HubConnection connection = await _host.ConnectDisplayAsync(display);

        TaskCompletionSource<string> revokedNotice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string>(nameof(IPlaybackClient.ReceiveDisplayRevoked), id => revokedNotice.TrySetResult(id));

        TaskCompletionSource<Exception?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += exception =>
        {
            closed.TrySetResult(exception);
            return Task.CompletedTask;
        };

        return new WatchedConnection(display.Display.Id, connection, revokedNotice.Task, closed.Task);
    }

    private static HttpRequestMessage CreateRequest(string endpoint, string ownerId)
    {
        return endpoint switch
        {
            LOGOUT => new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"),
            REVOKE_SESSION => new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{ownerId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.")
        };
    }

    private sealed record WatchedConnection(
        string DisplayId,
        HubConnection Connection,
        Task<string> RevokedNotice,
        Task<Exception?> Closed);
}
