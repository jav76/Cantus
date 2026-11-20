using Cantus.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public enum CredentialKind
{
    Session,
    Display
}

/// <summary>
/// Who is calling and what their credential allows. <see cref="Owner"/> is the Session the
/// caller acts for: the caller's own Session, or the Session a Display is paired to.
/// </summary>
public sealed record CallerCredential(CredentialKind Kind, UserSession Owner, DisplayInfo? Display)
{
    public bool IsSession => Kind == CredentialKind.Session;
}

public interface ICredentialResolver
{
    /// <summary>
    /// Resolves the request's credential to a caller. A valid Session wins over a Display
    /// token when both are present. Returns null for anonymous or unknown credentials.
    /// </summary>
    Task<CallerCredential?> ResolveAsync(HttpContext? httpContext, CancellationToken cancellationToken = default);
}
