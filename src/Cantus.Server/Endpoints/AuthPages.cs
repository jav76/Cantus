using System.Net;
using Cantus.Core.Models;

namespace Cantus.Server.Endpoints;

/// <summary>The HTML pages the Spotify sign-in flow ends on, in the browser that ran it.</summary>
internal static class AuthPages
{
    /// <summary>Opens the Connect Spotify setup dialog in the web player; letters only, so the client can strip it.</summary>
    public const string SETUP_QUERY = "connect=spotify";

    public static string Success(UserSession session)
    {
        string encodedDisplayName = WebUtility.HtmlEncode(session.DisplayName);
        string sessionId = session.Id;

        return Page(
            "Cantus - Spotify Connected",
            $$"""
        <div class="icon-badge">&#10003;</div>
        <h1>Spotify Connected</h1>
        <p>Logged in as <span class="user-name">{{encodedDisplayName}}</span>.<br>You can now return to the Cantus Desktop App, or continue in the Web Player.</p>
        <div class="btn-group">
            <a href="cantus://auth?session_id={{sessionId}}" class="btn btn-primary" id="open-app-btn">Return to Desktop App</a>
            <a href="/" class="btn btn-secondary">Open Web Player</a>
        </div>
    </div>
    <script>
        try {
            window.location.href = "cantus://auth?session_id={{sessionId}}";
        } catch (e) {
        }
    </script>
""");
    }

    /// <param name="redirectUri">Shown on failures that a redirect URI mismatch can cause.</param>
    /// <param name="offerOwnApp">Whether this server lets the user sign in with their own Spotify app.</param>
    public static string Failure(SpotifyAuthFailure failure, string? redirectUri, bool offerOwnApp)
    {
        (string title, string detail) = Describe(failure);
        bool showRedirectUri = !string.IsNullOrWhiteSpace(redirectUri)
            && failure is SpotifyAuthFailure.InvalidClientId or SpotifyAuthFailure.InvalidGrant;
        bool showOwnApp = offerOwnApp
            && failure is SpotifyAuthFailure.NotAllowlisted
                or SpotifyAuthFailure.NotConfigured
                or SpotifyAuthFailure.InvalidClientId;

        string redirectBlock = showRedirectUri
            ? $"""
        <p class="hint">Your Spotify app's Redirect URIs must include exactly:</p>
        <code>{WebUtility.HtmlEncode(redirectUri)}</code>
"""
            : string.Empty;
        string ownAppButton = showOwnApp
            ? $"""
            <a href="/?{SETUP_QUERY}" class="btn btn-primary">Set up your own Spotify app</a>
"""
            : string.Empty;
        string ownAppHint = showOwnApp
            ? """
        <p class="hint">In the desktop app, choose Connect Spotify, then Use my own Spotify app.</p>
"""
            : string.Empty;

        return Page(
            "Cantus - Spotify sign-in failed",
            $"""
        <div class="icon-badge error">!</div>
        <h1>{WebUtility.HtmlEncode(title)}</h1>
        <p>{WebUtility.HtmlEncode(detail)}</p>
{redirectBlock}{ownAppHint}        <div class="btn-group">
{ownAppButton}            <a href="/" class="btn btn-secondary">Back to Cantus</a>
        </div>
    </div>
""");
    }

    internal static (string Title, string Detail) Describe(SpotifyAuthFailure failure) => failure switch
    {
        SpotifyAuthFailure.Cancelled => (
            "Sign-in cancelled",
            "The Spotify consent screen was closed or declined. Nothing was changed."),
        SpotifyAuthFailure.NotConfigured => (
            "Spotify is not set up on this server",
            "This server has no Spotify app of its own. Use your own Spotify app, or ask the operator to set Spotify__ClientId."),
        SpotifyAuthFailure.InvalidClientId => (
            "Spotify did not accept this Client ID",
            "Copy the Client ID again from your app's Settings page in the Spotify Developer Dashboard. It is 32 letters and digits."),
        SpotifyAuthFailure.UserClientIdsDisabled => (
            "Own Spotify apps are turned off here",
            "This server only signs in through its own Spotify app. Ask the operator to add your Spotify account under User Management in their app."),
        SpotifyAuthFailure.NotAllowlisted => (
            "Your account is not on this app's user list",
            "Spotify apps in Development Mode only accept up to 5 users that the app owner adds by email. Ask the owner to add you under User Management, or use your own Spotify app."),
        SpotifyAuthFailure.OwnerNeedsPremium => (
            "The Spotify app owner needs Premium",
            "Spotify only runs Development Mode apps whose owner has Spotify Premium. Create the app with a Premium account."),
        SpotifyAuthFailure.InvalidGrant => (
            "Spotify rejected the sign-in",
            "This usually means the redirect URI in your Spotify app does not exactly match the one below. Fix it in the dashboard and try again."),
        SpotifyAuthFailure.SignInExpired => (
            "Sign-in expired",
            "The sign-in took longer than 10 minutes, or finished in a different browser from the one that started it. Start again from Cantus."),
        _ => (
            "Spotify sign-in failed",
            "Try again. If it keeps failing, the server log has the details."),
    };

    private static string Page(string title, string body)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{title}}</title>
    <style>
        :root {
            --bg-color: #0f172a;
            --card-bg: #1e293b;
            --accent-green: #10b981;
            --accent-red: #f87171;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --border-color: rgba(255, 255, 255, 0.1);
        }
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-color);
            color: var(--text-primary);
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            padding: 24px;
        }
        .card {
            background-color: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 40px;
            max-width: 480px;
            width: 100%;
            text-align: center;
            box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.5);
        }
        .icon-badge {
            width: 64px;
            height: 64px;
            border-radius: 50%;
            background: rgba(16, 185, 129, 0.1);
            border: 2px solid var(--accent-green);
            display: flex;
            align-items: center;
            justify-content: center;
            margin: 0 auto 24px auto;
            color: var(--accent-green);
            font-size: 32px;
            font-weight: bold;
        }
        h1 {
            font-size: 24px;
            font-weight: 700;
            margin-bottom: 8px;
            color: var(--text-primary);
        }
        .user-name {
            color: var(--accent-green);
            font-weight: 600;
        }
        p {
            color: var(--text-secondary);
            font-size: 15px;
            line-height: 1.5;
            margin-bottom: 28px;
        }
        .btn-group {
            display: flex;
            flex-direction: column;
            gap: 12px;
        }
        .btn {
            display: inline-block;
            padding: 12px 24px;
            border-radius: 8px;
            font-size: 15px;
            font-weight: 600;
            text-decoration: none;
            cursor: pointer;
            transition: all 0.2s ease;
        }
        .btn-primary {
            background-color: var(--accent-green);
            color: #042f1f;
            border: none;
        }
        .btn-primary:hover {
            background-color: #34d399;
            transform: translateY(-1px);
        }
        .btn-secondary {
            background: rgba(255, 255, 255, 0.05);
            color: var(--text-primary);
            border: 1px solid var(--border-color);
        }
        .btn-secondary:hover {
            background: rgba(255, 255, 255, 0.1);
        }
        .icon-badge.error {
            background: rgba(248, 113, 113, 0.1);
            border-color: var(--accent-red);
            color: var(--accent-red);
        }
        .hint {
            font-size: 14px;
            margin-top: -12px;
        }
        code {
            display: block;
            background: rgba(255, 255, 255, 0.05);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 10px 12px;
            margin: -12px 0 28px 0;
            font-size: 13px;
            word-break: break-all;
            color: var(--text-primary);
        }
    </style>
</head>
<body>
    <div class="card">
{{body}}</body>
</html>
""";
    }
}
