using System;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cantus.Client.Services;

/// <summary><see cref="ILocalPlaybackSink"/> over the app's hub connection.</summary>
public sealed class SignalRLocalPlaybackSink : ILocalPlaybackSink
{
    private readonly ISignalRPlaybackClient _client;

    public SignalRLocalPlaybackSink(ISignalRPlaybackClient client)
    {
        _client = client;
    }

    public bool IsConnected => _client.State == HubConnectionState.Connected;

    public string? ConnectionId => _client.ConnectionId;

    public long? ServerClockOffsetMs => _client.IsClockSynced ? _client.ClockOffsetMs : null;

    public event Action? Connected
    {
        add => _client.Connected += value;
        remove => _client.Connected -= value;
    }

    public Task<int> PublishAsync(LocalPlaybackUpdatePayload payload) => _client.PublishLocalPlaybackAsync(payload);
}
