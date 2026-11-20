using System.Buffers.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.Displays;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Displays;

public sealed class DisplayServiceTests : IDisposable
{
    private const int TOKEN_BYTE_LENGTH = 32;
    private const int LAST_SEEN_THROTTLE_SECONDS = 30;
    private const int LAST_SEEN_REFRESH_MINUTES = 2;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CantusDbContext> _options;
    private readonly CantusDbContext _dbContext;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly DisplayService _service;

    public DisplayServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(_options);
        _dbContext.Database.EnsureCreated();

        _service = new DisplayService(_dbContext, _timeProvider, NullLogger<DisplayService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ReturnsBase64UrlTokenOf32RandomBytes()
    {
        string owner = await SeedOwnerAsync();

        CreatedDisplay created = await _service.CreateAsync(owner, "Living room TV");

        Base64Url.DecodeFromChars(created.Token).Should().HaveCount(TOKEN_BYTE_LENGTH);
        created.Display.OwnerSessionId.Should().Be(owner);
        created.Display.Name.Should().Be("Living room TV");
        created.Display.CreatedAtUtc.Should().Be(_timeProvider.GetUtcNow());
        created.Display.LastSeenAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_StoresOnlyAHashOfTheToken()
    {
        string owner = await SeedOwnerAsync();

        CreatedDisplay created = await _service.CreateAsync(owner, "Kitchen");

        await using CantusDbContext readContext = new(_options);
        DisplayEntity stored = await readContext.Displays.SingleAsync(d => d.Id == created.Display.Id);
        stored.TokenHash.Should().NotBe(created.Token);
        stored.TokenHash.Should().NotContain(created.Token);
        stored.TokenHash.Should().HaveLength(DisplayEntity.TOKEN_HASH_LENGTH);
    }

    [Fact]
    public async Task CreateAsync_IssuesADifferentTokenEachTime()
    {
        string owner = await SeedOwnerAsync();

        CreatedDisplay first = await _service.CreateAsync(owner, "One");
        CreatedDisplay second = await _service.CreateAsync(owner, "Two");

        first.Token.Should().NotBe(second.Token);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownOwner_Throws()
    {
        Func<Task> act = () => _service.CreateAsync("missing-owner", "TV");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithBlankName_Throws(string name)
    {
        string owner = await SeedOwnerAsync();

        Func<Task> act = () => _service.CreateAsync(owner, name);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WithNameOverMaxLength_Throws()
    {
        string owner = await SeedOwnerAsync();

        Func<Task> act = () => _service.CreateAsync(owner, new string('x', DisplayInfo.MAX_NAME_LENGTH + 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateAsync_WithIssuedToken_ReturnsDisplayAndRecordsLastSeen()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        DisplayInfo? display = await _service.ValidateAsync(created.Token);

        display.Should().NotBeNull();
        display!.Id.Should().Be(created.Display.Id);
        display.OwnerSessionId.Should().Be(owner);
        display.LastSeenAtUtc.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ValidateAsync_ShortlyAfterLastSeen_DoesNotRewriteLastSeen()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");
        DateTimeOffset firstSeen = _timeProvider.GetUtcNow();
        await _service.ValidateAsync(created.Token);

        _timeProvider.Advance(TimeSpan.FromSeconds(LAST_SEEN_THROTTLE_SECONDS));
        DisplayInfo? display = await _service.ValidateAsync(created.Token);

        display!.LastSeenAtUtc.Should().Be(firstSeen);
    }

    [Fact]
    public async Task ValidateAsync_AfterTheUpdateInterval_RefreshesLastSeen()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");
        await _service.ValidateAsync(created.Token);

        _timeProvider.Advance(TimeSpan.FromMinutes(LAST_SEEN_REFRESH_MINUTES));
        DisplayInfo? display = await _service.ValidateAsync(created.Token);

        display!.LastSeenAtUtc.Should().Be(_timeProvider.GetUtcNow());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task ValidateAsync_WithUnknownToken_ReturnsNull(string token)
    {
        string owner = await SeedOwnerAsync();
        await _service.CreateAsync(owner, "TV");

        DisplayInfo? display = await _service.ValidateAsync(token);

        display.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_WithTheStoredHash_ReturnsNull()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");
        DisplayEntity stored = await _dbContext.Displays.AsNoTracking().SingleAsync(d => d.Id == created.Display.Id);

        DisplayInfo? display = await _service.ValidateAsync(stored.TokenHash);

        display.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheOwnersDisplaysOldestFirst()
    {
        string owner = await SeedOwnerAsync();
        string otherOwner = await SeedOwnerAsync();
        CreatedDisplay first = await _service.CreateAsync(owner, "First");
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        CreatedDisplay second = await _service.CreateAsync(owner, "Second");
        await _service.CreateAsync(otherOwner, "Not mine");

        IReadOnlyList<DisplayInfo> displays = await _service.ListAsync(owner);

        displays.Select(d => d.Id).Should().Equal(first.Display.Id, second.Display.Id);
    }

    [Fact]
    public async Task RenameAsync_ByOwner_ChangesName()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        bool renamed = await _service.RenameAsync(owner, created.Display.Id, "Bedroom TV");

        renamed.Should().BeTrue();
        (await _service.ListAsync(owner)).Single().Name.Should().Be("Bedroom TV");
    }

    [Fact]
    public async Task RenameAsync_ByAnotherOwner_ReturnsFalseAndKeepsName()
    {
        string owner = await SeedOwnerAsync();
        string otherOwner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        bool renamed = await _service.RenameAsync(otherOwner, created.Display.Id, "Hijacked");

        renamed.Should().BeFalse();
        (await _service.ListAsync(owner)).Single().Name.Should().Be("TV");
    }

    [Fact]
    public async Task RevokeAsync_ByOwner_InvalidatesTokenAndKeepsOwnerSession()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        bool revoked = await _service.RevokeAsync(owner, created.Display.Id);

        revoked.Should().BeTrue();
        (await _service.ValidateAsync(created.Token)).Should().BeNull();
        (await _dbContext.UserSessions.AnyAsync(s => s.Id == owner)).Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_ByAnotherOwner_ReturnsFalseAndKeepsDisplay()
    {
        string owner = await SeedOwnerAsync();
        string otherOwner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        bool revoked = await _service.RevokeAsync(otherOwner, created.Display.Id);

        revoked.Should().BeFalse();
        (await _service.ValidateAsync(created.Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeletingTheOwnerSession_RemovesItsDisplays()
    {
        string owner = await SeedOwnerAsync();
        CreatedDisplay created = await _service.CreateAsync(owner, "TV");

        await using (CantusDbContext deleteContext = new(_options))
        {
            UserSessionEntity session = await deleteContext.UserSessions.SingleAsync(s => s.Id == owner);
            deleteContext.UserSessions.Remove(session);
            await deleteContext.SaveChangesAsync();
        }

        _dbContext.ChangeTracker.Clear();
        (await _service.ValidateAsync(created.Token)).Should().BeNull();
    }

    [Fact]
    public void CreatedDisplay_ToString_RedactsToken()
    {
        CreatedDisplay created = new(
            new DisplayInfo { Id = "d1", OwnerSessionId = "owner-secret", Name = "TV" },
            "plain-token-value");

        string text = created.ToString();

        text.Should().NotContain("plain-token-value");
        text.Should().NotContain("owner-secret");
    }

    private async Task<string> SeedOwnerAsync()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        UserSessionEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SpotifyUserId = "sp_" + Guid.NewGuid().ToString("N"),
            DisplayName = "Owner",
            EncryptedAccessToken = "encrypted-access",
            EncryptedRefreshToken = "encrypted-refresh",
            ExpiresAtUtc = now.AddHours(1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using CantusDbContext seedContext = new(_options);
        await seedContext.UserSessions.AddAsync(entity);
        await seedContext.SaveChangesAsync();
        return entity.Id;
    }
}
