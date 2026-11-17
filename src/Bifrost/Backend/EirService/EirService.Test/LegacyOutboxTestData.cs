using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

// Historical schemas deliberately lack the current EF entity's additive delivery columns.
internal static class LegacyOutboxTestData
{
    private static string Table(string schema) => schema switch
    {
        "sessions" => "sessions.outbox",
        "helprequests" => "helprequests.outbox",
        _ => throw new ArgumentOutOfRangeException(nameof(schema))
    };

    public static async Task InsertAsync(DbContext db, string schema, params OutboxMessage[] messages)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var row in messages)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"INSERT INTO {Table(schema)} (\"Id\", \"MessageId\", \"MessageType\", \"Payload\", \"ExchangeName\", \"RoutingKey\", \"CreatedAtUtc\", \"PublishedAtUtc\", \"LastAttemptAtUtc\", \"TraceParent\", \"TraceState\") VALUES (@id, @message, @type, @payload, @exchange, @route, @created, @published, @attempted, @parent, @state)";
                command.Parameters.Add(new NpgsqlParameter("id", row.Id));
                command.Parameters.Add(new NpgsqlParameter("message", row.MessageId));
                command.Parameters.Add(new NpgsqlParameter("type", row.MessageType));
                command.Parameters.Add(new NpgsqlParameter("payload", row.Payload));
                command.Parameters.Add(new NpgsqlParameter("exchange", row.ExchangeName));
                command.Parameters.Add(new NpgsqlParameter("route", row.RoutingKey));
                command.Parameters.Add(new NpgsqlParameter("created", row.CreatedAtUtc));
                command.Parameters.Add(new NpgsqlParameter("published", NpgsqlDbType.TimestampTz) { Value = (object?)row.PublishedAtUtc ?? DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter("attempted", NpgsqlDbType.TimestampTz) { Value = (object?)row.LastAttemptAtUtc ?? DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter("parent", NpgsqlDbType.Text) { Value = (object?)row.TraceParent ?? DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)row.TraceState ?? DBNull.Value });
                await command.ExecuteNonQueryAsync();
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public static async Task<Dictionary<Guid, Row>> ReadAsync(DbContext db, string schema)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT \"Id\", \"MessageId\", \"MessageType\", \"Payload\", \"ExchangeName\", \"RoutingKey\", \"CreatedAtUtc\", \"PublishedAtUtc\", \"LastAttemptAtUtc\", \"TraceParent\", \"TraceState\" FROM {Table(schema)}";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new Dictionary<Guid, Row>();
            while (await reader.ReadAsync())
            {
                var row = new Row(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10));
                rows.Add(row.Id, row);
            }
            return rows;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    internal sealed record Row(Guid Id, Guid MessageId, string MessageType, string Payload, string ExchangeName,
        string RoutingKey, DateTimeOffset CreatedAtUtc, DateTimeOffset? PublishedAtUtc, DateTimeOffset? LastAttemptAtUtc,
        string? TraceParent, string? TraceState);
}
