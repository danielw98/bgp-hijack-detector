using System.Net;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using Npgsql;
using NpgsqlTypes;

namespace BGPWatch.Web.Storage;

/// <summary>Route updates and alerts in Postgres (detector/schema.sql), written with binary COPY.</summary>
public sealed class RouteLog(NpgsqlDataSource database) : IRouteLog
{
    private const string CopyUpdates =
        "COPY route_updates (seen_at, peer, prefix, origin_as, as_path, next_hop, withdrawn, source) FROM STDIN (FORMAT BINARY)";

    private const string CopyAlerts =
        "COPY alerts (raised_at, alert_type, prefix, observed_origin, expected_origins, covering_prefix, peer, as_path, detail, source) FROM STDIN (FORMAT BINARY)";

    private const NpgsqlDbType IntegerArray = NpgsqlDbType.Array | NpgsqlDbType.Integer;

    /// <summary>Runs the schema file; every statement in it is idempotent.</summary>
    public static async Task EnsureSchemaAsync(NpgsqlDataSource database, string schemaPath, CancellationToken cancel)
    {
        await using var command = database.CreateCommand(await File.ReadAllTextAsync(schemaPath, cancel));
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel)
    {
        foreach (var watched in monitored)
        {
            await using var command = database.CreateCommand("""
                INSERT INTO monitored_prefixes (prefix, expected_origins, description)
                VALUES ($1, $2, $3)
                ON CONFLICT (prefix) DO UPDATE
                  SET expected_origins = EXCLUDED.expected_origins,
                      description = EXCLUDED.description
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = watched.Prefix, NpgsqlDbType = NpgsqlDbType.Cidr });
            command.Parameters.Add(new NpgsqlParameter { Value = watched.ExpectedOrigins.Order().ToArray(), NpgsqlDbType = IntegerArray });
            command.Parameters.Add(new NpgsqlParameter { Value = watched.Description });
            await command.ExecuteNonQueryAsync(cancel);
        }
    }

    public async Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
    {
        await using var connection = await database.OpenConnectionAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await WriteAsync(connection, updates, alerts, source, cancel);
        await transaction.CommitAsync(cancel);
    }

    public async Task ReplaceAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
    {
        await using var connection = await database.OpenConnectionAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using (var delete = new NpgsqlBatch(connection, transaction))
        {
            delete.BatchCommands.Add(new NpgsqlBatchCommand("DELETE FROM alerts WHERE source = $1") { Parameters = { new() { Value = source } } });
            delete.BatchCommands.Add(new NpgsqlBatchCommand("DELETE FROM route_updates WHERE source = $1") { Parameters = { new() { Value = source } } });
            await delete.ExecuteNonQueryAsync(cancel);
        }
        await WriteAsync(connection, updates, alerts, source, cancel);
        await transaction.CommitAsync(cancel);
    }

    public async Task<SourceCounts> CountAsync(string source, CancellationToken cancel)
    {
        await using var command = database.CreateCommand("""
            SELECT (SELECT count(*) FROM route_updates WHERE source = $1),
                   (SELECT count(*) FROM alerts WHERE source = $1)
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = source });
        await using var reader = await command.ExecuteReaderAsync(cancel);
        await reader.ReadAsync(cancel);
        return new SourceCounts(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task WriteAsync(NpgsqlConnection connection, IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts,
        string source, CancellationToken cancel)
    {
        await WriteUpdatesAsync(connection, updates, source, cancel);
        if (alerts.Count > 0)
        {
            await WriteAlertsAsync(connection, alerts, source, cancel);
        }
    }

    private static async Task WriteUpdatesAsync(NpgsqlConnection connection, IReadOnlyList<Announcement> updates, string source, CancellationToken cancel)
    {
        await using var writer = await connection.BeginBinaryImportAsync(CopyUpdates, cancel);
        foreach (var update in updates)
        {
            await writer.StartRowAsync(cancel);
            await writer.WriteAsync(update.SeenAt, NpgsqlDbType.TimestampTz, cancel);
            await writer.WriteAsync(update.Peer, NpgsqlDbType.Text, cancel);
            await writer.WriteAsync(update.Prefix, NpgsqlDbType.Cidr, cancel);
            await WriteNullableAsync(writer, update.OriginAs, NpgsqlDbType.Integer, cancel);
            await writer.WriteAsync(update.AsPath.ToArray(), IntegerArray, cancel);
            await WriteNullableAsync(writer, update.NextHop is null ? null : IPAddress.Parse(update.NextHop), NpgsqlDbType.Inet, cancel);
            await writer.WriteAsync(update.Withdrawn, NpgsqlDbType.Boolean, cancel);
            await writer.WriteAsync(source, NpgsqlDbType.Text, cancel);
        }
        await writer.CompleteAsync(cancel);
    }

    private static async Task WriteAlertsAsync(NpgsqlConnection connection, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
    {
        await using var writer = await connection.BeginBinaryImportAsync(CopyAlerts, cancel);
        foreach (var alert in alerts)
        {
            await writer.StartRowAsync(cancel);
            await writer.WriteAsync(alert.RaisedAt, NpgsqlDbType.TimestampTz, cancel);
            await writer.WriteAsync(alert.Type.Name(), NpgsqlDbType.Text, cancel);
            await writer.WriteAsync(alert.Prefix, NpgsqlDbType.Cidr, cancel);
            await WriteNullableAsync(writer, alert.ObservedOrigin, NpgsqlDbType.Integer, cancel);
            await writer.WriteAsync(alert.ExpectedOrigins.Order().ToArray(), IntegerArray, cancel);
            await WriteNullableAsync(writer, alert.CoveringPrefix, NpgsqlDbType.Cidr, cancel);
            await writer.WriteAsync(alert.Peer, NpgsqlDbType.Text, cancel);
            await writer.WriteAsync(alert.AsPath.ToArray(), IntegerArray, cancel);
            await writer.WriteAsync(alert.Detail, NpgsqlDbType.Text, cancel);
            await writer.WriteAsync(source, NpgsqlDbType.Text, cancel);
        }
        await writer.CompleteAsync(cancel);
    }

    private static async Task WriteNullableAsync<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancel)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancel);
            return;
        }
        await writer.WriteAsync(value, type, cancel);
    }
}
