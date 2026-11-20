using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class DisplayPairingServiceTests
{
    private const string CONNECTION_ID = "conn-1";
    private const string OTHER_CONNECTION_ID = "conn-2";
    private const string UNAMBIGUOUS_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LIFETIME_SECONDS = 120;
    private const int CODE_SAMPLE_COUNT = 200;

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly DisplayPairingOptions _options = new() { CodeLifetimeSeconds = LIFETIME_SECONDS };

    [Fact]
    public void Request_ReturnsCodeOfConfiguredLengthFromUnambiguousAlphabet()
    {
        DisplayPairingService service = CreateService();

        for (int i = 0; i < CODE_SAMPLE_COUNT; i++)
        {
            PendingDisplayPairing pairing = service.Request($"conn-{i}")!;

            pairing.Code.Should().HaveLength(_options.CodeLength);
            pairing.Code.Should().Match(code => code.All(c => UNAMBIGUOUS_ALPHABET.Contains(c)));
        }
    }

    [Fact]
    public void Request_ReturnsPairingIdConnectionAndExpiry()
    {
        DisplayPairingService service = CreateService();

        PendingDisplayPairing pairing = service.Request(CONNECTION_ID)!;

        pairing.PairingId.Should().NotBeNullOrWhiteSpace();
        pairing.ConnectionId.Should().Be(CONNECTION_ID);
        pairing.ExpiresAtUtc.Should().Be(_timeProvider.GetUtcNow().AddSeconds(LIFETIME_SECONDS));
    }

    [Fact]
    public void Claim_ReturnsPairingOnce()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing pairing = service.Request(CONNECTION_ID)!;

        PendingDisplayPairing? first = service.Claim(pairing.Code);
        PendingDisplayPairing? second = service.Claim(pairing.Code);

        first.Should().Be(pairing);
        second.Should().BeNull();
    }

    [Fact]
    public void Claim_IgnoresCaseSpacesAndHyphens()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing pairing = service.Request(CONNECTION_ID)!;
        string typed = $" {pairing.Code[..4].ToLowerInvariant()}-{pairing.Code[4..]} ";

        service.Claim(typed).Should().Be(pairing);
    }

    [Fact]
    public void Claim_AfterLifetime_ReturnsNull()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing pairing = service.Request(CONNECTION_ID)!;

        _timeProvider.Advance(TimeSpan.FromSeconds(LIFETIME_SECONDS));

        service.Claim(pairing.Code).Should().BeNull();
    }

    [Fact]
    public void Claim_JustBeforeLifetime_ReturnsPairing()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing pairing = service.Request(CONNECTION_ID)!;

        _timeProvider.Advance(TimeSpan.FromSeconds(LIFETIME_SECONDS - 1));

        service.Claim(pairing.Code).Should().Be(pairing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOTACODE")]
    public void Claim_UnknownCode_ReturnsNull(string code)
    {
        DisplayPairingService service = CreateService();
        service.Request(CONNECTION_ID);

        service.Claim(code).Should().BeNull();
    }

    [Fact]
    public void Request_AgainForSameConnection_ReplacesPreviousCode()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing first = service.Request(CONNECTION_ID)!;

        PendingDisplayPairing second = service.Request(CONNECTION_ID)!;

        service.Claim(first.Code).Should().BeNull();
        service.Claim(second.Code).Should().Be(second);
    }

    [Fact]
    public void Cancel_DropsOnlyThatConnectionsPairing()
    {
        DisplayPairingService service = CreateService();
        PendingDisplayPairing cancelled = service.Request(CONNECTION_ID)!;
        PendingDisplayPairing kept = service.Request(OTHER_CONNECTION_ID)!;

        service.Cancel(CONNECTION_ID);

        service.Claim(cancelled.Code).Should().BeNull();
        service.Claim(kept.Code).Should().Be(kept);
    }

    [Fact]
    public void Request_WhenPendingLimitReached_ReturnsNull()
    {
        _options.MaxPendingPairings = 2;
        DisplayPairingService service = CreateService();
        service.Request("conn-a");
        service.Request("conn-b");

        service.Request("conn-c").Should().BeNull();
    }

    [Fact]
    public void Request_WhenPendingLimitReachedByExpiredPairings_PurgesThemAndSucceeds()
    {
        _options.MaxPendingPairings = 2;
        DisplayPairingService service = CreateService();
        service.Request("conn-a");
        service.Request("conn-b");
        _timeProvider.Advance(TimeSpan.FromSeconds(LIFETIME_SECONDS));

        service.Request("conn-c").Should().NotBeNull();
    }

    private DisplayPairingService CreateService()
    {
        return new DisplayPairingService(Options.Create(_options), _timeProvider);
    }
}
