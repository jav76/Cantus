using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// Owners list, rename and revoke their Displays. Revoking closes the Display's live hub
/// connections and leaves the owner's Session and its connections alone.
/// </summary>
public sealed class DisplayManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string LIST = "GET /api/displays";
    private const string RENAME = "PATCH /api/displays/{displayId}";
    private const string REVOKE = "DELETE /api/displays/{displayId}";
    private const string NEW_NAME = "Bedroom";

    private readonly DisplayTestHost _host;

    public DisplayManagementEndpointTests(WebApplicationFactory<Program> factory)
    {
        _host = new DisplayTestHost(factory);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    public static TheoryData<string> Endpoints() => new() { LIST, RENAME, REVOKE };

    [Fact]
    public async Task List_WithSession_ReturnsOnlyOwnersDisplaysWithoutSecrets()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        UserSessionEntity other = await _host.Store.SeedSessionAsync("Other");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        await _host.Store.CreateDisplayService().CreateAsync(other.Id, "Not mine");

        HttpResponseMessage response = await _host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id)
            .GetAsync(DisplayTestHost.DISPLAYS_PATH);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(owner.Id).And.NotContain(tv.Token);
        List<DisplayDto> displays = (await response.Content.ReadFromJsonAsync<List<DisplayDto>>())!;
        displays.Should().ContainSingle().Which.Should().BeEquivalentTo(new { tv.Display.Id, Name = "TV" });
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task OwnerEndpoint_WithDisplayToken_ReturnsForbiddenAndChangesNothing(string endpoint)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        HttpClient client = _host.CreateClient(DisplayTestHost.DISPLAY_COOKIE, tv.Token);

        HttpResponseMessage response = await client.SendAsync(CreateRequest(endpoint, tv.Display.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id))
            .Should().ContainSingle().Which.Name.Should().Be("TV");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task OwnerEndpoint_WithoutCredential_ReturnsUnauthorized(string endpoint)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        HttpResponseMessage response = await _host.CreateClient().SendAsync(CreateRequest(endpoint, tv.Display.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rename_WithSession_RenamesDisplay()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        HttpClient client = _host.CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id);

        HttpResponseMessage response = await client.SendAsync(CreateRequest(RENAME, tv.Display.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id))
            .Should().ContainSingle().Which.Name.Should().Be(NEW_NAME);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task Rename_WithInvalidName_ReturnsBadRequest(string? name)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        HttpClient client = _host.CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DisplayTestHost.DISPLAYS_PATH}/{tv.Display.Id}",
            new DisplayRenameDto { Name = name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(RENAME)]
    [InlineData(REVOKE)]
    public async Task OwnerEndpoint_ForAnotherOwnersDisplay_ReturnsNotFoundAndChangesNothing(string endpoint)
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        UserSessionEntity other = await _host.Store.SeedSessionAsync("Other");
        CreatedDisplay theirs = await _host.Store.CreateDisplayService().CreateAsync(other.Id, "TV");
        HttpClient client = _host.CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id);

        HttpResponseMessage response = await client.SendAsync(CreateRequest(endpoint, theirs.Display.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.Store.CreateDisplayService().ListAsync(other.Id))
            .Should().ContainSingle().Which.Name.Should().Be("TV");
    }

    [Fact]
    public async Task Revoke_WithSession_NotifiesAndClosesDisplayConnectionsAndKeepsSession()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        HubConnection ownerConnection = await _host.ConnectAsync(owner.Id);
        HubConnection first = await _host.ConnectDisplayAsync(tv);
        HubConnection second = await _host.ConnectDisplayAsync(tv);
        TaskCompletionSource<string> revokedNotice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        first.On<string>(nameof(IPlaybackClient.ReceiveDisplayRevoked), id => revokedNotice.TrySetResult(id));
        Task<Exception?> firstClosed = ClosedAsync(first);
        Task<Exception?> secondClosed = ClosedAsync(second);

        HttpResponseMessage response = await _host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id)
            .SendAsync(CreateRequest(REVOKE, tv.Display.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DisplayTestHost.WaitAsync(revokedNotice.Task)).Should().Be(tv.Display.Id);
        await DisplayTestHost.WaitAsync(firstClosed);
        await DisplayTestHost.WaitAsync(secondClosed);
        first.State.Should().Be(HubConnectionState.Disconnected);
        second.State.Should().Be(HubConnectionState.Disconnected);

        ownerConnection.State.Should().Be(HubConnectionState.Connected);
        (await _host.Store.SessionExistsAsync(owner.Id)).Should().BeTrue();
        (await _host.Store.CreateDisplayService().ValidateAsync(tv.Token)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_WithSession_LeavesOwnersOtherDisplaysConnected()
    {
        UserSessionEntity owner = await _host.Store.SeedSessionAsync("Owner");
        CreatedDisplay revoked = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        CreatedDisplay kept = await _host.Store.CreateDisplayService().CreateAsync(owner.Id, "Kitchen");
        HubConnection revokedConnection = await _host.ConnectDisplayAsync(revoked);
        HubConnection keptConnection = await _host.ConnectDisplayAsync(kept);
        Task<Exception?> revokedClosed = ClosedAsync(revokedConnection);

        await _host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, owner.Id)
            .SendAsync(CreateRequest(REVOKE, revoked.Display.Id));

        await DisplayTestHost.WaitAsync(revokedClosed);
        keptConnection.State.Should().Be(HubConnectionState.Connected);
        (await _host.Store.CreateDisplayService().ListAsync(owner.Id))
            .Should().ContainSingle().Which.Id.Should().Be(kept.Display.Id);
    }

    private static Task<Exception?> ClosedAsync(HubConnection connection)
    {
        TaskCompletionSource<Exception?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += exception =>
        {
            closed.TrySetResult(exception);
            return Task.CompletedTask;
        };
        return closed.Task;
    }

    private static HttpRequestMessage CreateRequest(string endpoint, string displayId)
    {
        string path = $"{DisplayTestHost.DISPLAYS_PATH}/{displayId}";
        return endpoint switch
        {
            LIST => new HttpRequestMessage(HttpMethod.Get, DisplayTestHost.DISPLAYS_PATH),
            RENAME => new HttpRequestMessage(HttpMethod.Patch, path)
            {
                Content = JsonContent.Create(new DisplayRenameDto { Name = NEW_NAME }),
            },
            REVOKE => new HttpRequestMessage(HttpMethod.Delete, path),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.")
        };
    }
}
