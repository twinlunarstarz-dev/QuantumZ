using Microsoft.Data.Sqlite;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using System.Collections.Generic;

namespace QuantumZ.Infrastructure.Services;

public sealed class SqliteLogBufferService : IActivityLogger
{
    private readonly string _dbPath;

    public SqliteLogBufferService()
    {
        _dbPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "activity_logs.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = 
            @"CREATE TABLE IF NOT EXISTS LogEntries (
                Id TEXT PRIMARY KEY,
                Text TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                Metadata TEXT
            );";
        command.ExecuteNonQuery();
    }

    public async ValueTask LogFragmentAsync(string text, string? metadata = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO LogEntries (Id, Text, Timestamp, Metadata) VALUES (@id, @text, @timestamp, @metadata)";
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@text", text);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@metadata", metadata ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask<List<LogEntry>> GetLogsAsync(DateTime start, DateTime end)
    {
        var logs = new List<LogEntry>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Text, Timestamp, Metadata FROM LogEntries WHERE Timestamp BETWEEN @start AND @end ORDER BY Timestamp ASC";
        command.Parameters.AddWithValue("@start", start.ToString("O"));
        command.Parameters.AddWithValue("@end", end.ToString("O"));

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new LogEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }
        return logs;
    }

    public async ValueTask ClearLogsAsync(DateTime before)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LogEntries WHERE Timestamp < @before";
        command.Parameters.AddWithValue("@before", before.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}