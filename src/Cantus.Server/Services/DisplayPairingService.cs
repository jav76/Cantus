using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

public sealed class DisplayPairingService : IDisplayPairingService
{
    // No 0/O, 1/I/L: the code is read off a TV across a room and typed on a phone.
    private const string CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly Dictionary<string, PendingDisplayPairing> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeByConnection = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly DisplayPairingOptions _options;
    private readonly TimeProvider _timeProvider;

    public DisplayPairingService(IOptions<DisplayPairingOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public PendingDisplayPairing? Request(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            RemoveForConnection(connectionId);

            if (_byCode.Count >= _options.MaxPendingPairings)
            {
                PurgeExpired(now);
                if (_byCode.Count >= _options.MaxPendingPairings)
                {
                    return null;
                }
            }

            string code = GenerateUnusedCode();
            PendingDisplayPairing pairing = new(
                Guid.NewGuid().ToString("N"),
                code,
                connectionId,
                now.AddSeconds(_options.CodeLifetimeSeconds));

            _byCode[code] = pairing;
            _codeByConnection[connectionId] = code;
            return pairing;
        }
    }

    public PendingDisplayPairing? Claim(string code)
    {
        string normalized = Normalize(code);
        if (normalized.Length == 0)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (!_byCode.Remove(normalized, out PendingDisplayPairing? pairing))
            {
                return null;
            }

            _codeByConnection.Remove(pairing.ConnectionId);
            return IsExpired(pairing, now) ? null : pairing;
        }
    }

    public void Cancel(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        lock (_lock)
        {
            RemoveForConnection(connectionId);
        }
    }

    private void RemoveForConnection(string connectionId)
    {
        if (_codeByConnection.Remove(connectionId, out string? code))
        {
            _byCode.Remove(code);
        }
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        List<PendingDisplayPairing> expired = _byCode.Values
            .Where(pairing => IsExpired(pairing, now))
            .ToList();

        foreach (PendingDisplayPairing pairing in expired)
        {
            _byCode.Remove(pairing.Code);
            _codeByConnection.Remove(pairing.ConnectionId);
        }
    }

    private string GenerateUnusedCode()
    {
        string code;
        do
        {
            code = RandomNumberGenerator.GetString(CODE_ALPHABET, _options.CodeLength);
        }
        while (_byCode.ContainsKey(code));

        return code;
    }

    private static bool IsExpired(PendingDisplayPairing pairing, DateTimeOffset now) => now >= pairing.ExpiresAtUtc;

    private static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        StringBuilder builder = new(code.Length);
        foreach (char c in code)
        {
            if (!char.IsWhiteSpace(c) && c != '-')
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}
