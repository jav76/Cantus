using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cantus.Infrastructure.Persistence.Entities;
using log4net;
using log4net.Appender;
using log4net.Core;
using Microsoft.Data.Sqlite;

namespace Cantus.Infrastructure.Logging;

public class SqliteBatchLogAppender : AppenderSkeleton
{
    private readonly Channel<LogEntryEntity> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _workerTask;
    private string _connectionString = "Data Source=cantus.db";

    public string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value;
    }

    public SqliteBatchLogAppender()
    {
        _channel = Channel.CreateUnbounded<LogEntryEntity>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        _cts = new();
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    protected override void Append(LoggingEvent loggingEvent)
    {
        string? traceId = Activity.Current?.TraceId.ToString()
            ?? LogicalThreadContext.Properties["TraceId"]?.ToString();

        string? exceptionStr = loggingEvent.GetExceptionString();
        if (string.IsNullOrWhiteSpace(exceptionStr))
        {
            exceptionStr = null;
        }

        LogEntryEntity entry = new()
        {
            TimestampUtc = loggingEvent.TimeStampUtc,
            Level = loggingEvent.Level?.Name ?? "INFO",
            Logger = loggingEvent.LoggerName ?? string.Empty,
            Message = loggingEvent.RenderedMessage ?? string.Empty,
            Exception = exceptionStr,
            TraceId = traceId
        };

        _channel.Writer.TryWrite(entry);
    }

    private async Task ProcessQueueAsync()
    {
        List<LogEntryEntity> batch = new();

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(_cts.Token))
                {
                    while (_channel.Reader.TryRead(out LogEntryEntity? item) && batch.Count < 200)
                    {
                        batch.Add(item);
                    }

                    if (batch.Count > 0)
                    {
                        WriteBatchToDatabase(batch);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Suppress background logging failure to avoid crashing logging pipeline
            }
        }

        // Drain remaining items on shutdown
        while (_channel.Reader.TryRead(out LogEntryEntity? remaining))
        {
            batch.Add(remaining);
        }

        if (batch.Count > 0)
        {
            try
            {
                WriteBatchToDatabase(batch);
            }
            catch
            {
            }
        }
    }

    private void WriteBatchToDatabase(List<LogEntryEntity> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        using (SqliteConnection connection = new(_connectionString))
        {
            connection.Open();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO LogEntries (TimestampUtc, Level, Logger, Message, Exception, TraceId)
                        VALUES (@TimestampUtc, @Level, @Logger, @Message, @Exception, @TraceId)
                        """;

                    SqliteParameter pTimestamp = command.Parameters.Add("@TimestampUtc", SqliteType.Text);
                    SqliteParameter pLevel = command.Parameters.Add("@Level", SqliteType.Text);
                    SqliteParameter pLogger = command.Parameters.Add("@Logger", SqliteType.Text);
                    SqliteParameter pMessage = command.Parameters.Add("@Message", SqliteType.Text);
                    SqliteParameter pException = command.Parameters.Add("@Exception", SqliteType.Text);
                    SqliteParameter pTraceId = command.Parameters.Add("@TraceId", SqliteType.Text);

                    foreach (LogEntryEntity entry in entries)
                    {
                        pTimestamp.Value = entry.TimestampUtc.ToString("o");
                        pLevel.Value = entry.Level;
                        pLogger.Value = entry.Logger;
                        pMessage.Value = entry.Message;
                        pException.Value = (object?)entry.Exception ?? DBNull.Value;
                        pTraceId.Value = (object?)entry.TraceId ?? DBNull.Value;

                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }
    }

    protected override void OnClose()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }
        base.OnClose();
    }
}
