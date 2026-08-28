using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cantus.Infrastructure.Logging;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using FluentAssertions;
using log4net.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantus.Infrastructure.Tests.Logging;

public class SqliteBatchLogAppenderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly CantusDbContext _dbContext;

    public SqliteBatchLogAppenderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cantus_log_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        _dbContext = new CantusDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task SqliteBatchLogAppender_WhenAppendingEvents_PersistsLogEntriesToDatabase()
    {
        // Arrange
        SqliteBatchLogAppender appender = new()
        {
            ConnectionString = _connectionString
        };
        appender.ActivateOptions();

        LoggingEventData eventData = new()
        {
            LoggerName = "Cantus.Tests.TestLogger",
            Level = Level.Debug,
            Message = "Test batch log entry",
            TimeStampUtc = DateTime.UtcNow
        };
        LoggingEvent logEvent = new(eventData);

        // Act
        appender.DoAppend(logEvent);
        appender.Close(); // Flushes remaining queue and waits for worker

        // Give worker task a moment to finish writing
        await Task.Delay(100);

        // Assert
        LogEntryEntity? entry = await _dbContext.LogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Message == "Test batch log entry");

        entry.Should().NotBeNull();
        entry!.Level.Should().Be("DEBUG");
        entry.Logger.Should().Be("Cantus.Tests.TestLogger");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
            }
        }
    }
}
