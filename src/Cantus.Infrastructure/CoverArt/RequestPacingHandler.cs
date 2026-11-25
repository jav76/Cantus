namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// Holds every request of one typed client until <see cref="RequestPacer"/> lets it through.
/// Added per client with <c>AddHttpMessageHandler</c>; never register it as a service.
/// </summary>
public sealed class RequestPacingHandler : DelegatingHandler
{
    private readonly RequestPacer _pacer;

    public RequestPacingHandler(RequestPacer pacer)
    {
        ArgumentNullException.ThrowIfNull(pacer);
        _pacer = pacer;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _pacer.WaitForTurnAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
