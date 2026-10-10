using System.Data.Common;
using BGPWatch.Core.Domain;
using Npgsql;

namespace BGPWatch.Web.Storage;

/// <summary>The reads behind the dashboard API.</summary>
public sealed class Queries(NpgsqlDataSource database)
{
    private const string AlertColumns = """
        id, raised_at, alert_type, prefix::text AS prefix, observed_origin, expected_origins,
        covering_prefix::text AS covering_prefix, peer, as_path, detail, source
        """;

    private const string RouteColumns = """
        id, seen_at, peer, prefix::text AS prefix, origin_as, as_path,
        next_hop::text AS next_hop, withdrawn, source
        """;

    public async Task<Stats> StatsAsync(CancellationToken cancel)
    {
        var byType = new Dictionary<string, long>();
        await using (var command = database.CreateCommand("SELECT alert_type, count(*) AS c FROM alerts GROUP BY alert_type"))
        await using (var reader = await command.ExecuteReaderAsync(cancel))
        {
            while (await reader.ReadAsync(cancel))
            {
                byType[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        await using var totals = database.CreateCommand("""
            SELECT (SELECT count(*) FROM alerts),
                   (SELECT count(*) FROM route_updates),
                   (SELECT count(*) FROM monitored_prefixes)
            """);
        await using var row = await totals.ExecuteReaderAsync(cancel);
        await row.ReadAsync(cancel);
        return new Stats(byType, row.GetInt64(0), row.GetInt64(1), row.GetInt64(2));
    }

    public Task<List<StoredAlert>> AlertsAsync(int limit, CancellationToken cancel)
    {
        return ListAsync($"SELECT {AlertColumns} FROM alerts ORDER BY id DESC LIMIT $1", ReadAlert, cancel, limit);
    }

    public Task<List<StoredAlert>> AlertsWithinAsync(string prefix, int limit, CancellationToken cancel)
    {
        return ListAsync($"SELECT {AlertColumns} FROM alerts WHERE prefix <<= $1::cidr ORDER BY id DESC LIMIT $2",
            ReadAlert, cancel, prefix, limit);
    }

    public Task<List<StoredRoute>> RoutesAsync(int limit, CancellationToken cancel)
    {
        return ListAsync($"SELECT {RouteColumns} FROM route_updates ORDER BY id DESC LIMIT $1", ReadRoute, cancel, limit);
    }

    public Task<List<StoredRoute>> RoutesWithinAsync(string prefix, int limit, CancellationToken cancel)
    {
        return ListAsync($"SELECT {RouteColumns} FROM route_updates WHERE prefix <<= $1::cidr ORDER BY seen_at DESC, id DESC LIMIT $2",
            ReadRoute, cancel, prefix, limit);
    }

    /// <summary>The routes of a source inside any of the prefixes, in time order.</summary>
    public Task<List<StoredRoute>> SourceRoutesWithinAsync(string source, string[] prefixes, CancellationToken cancel)
    {
        return ListAsync($"SELECT {RouteColumns} FROM route_updates WHERE source = $1 AND prefix <<= ANY($2::cidr[]) ORDER BY seen_at, id",
            ReadRoute, cancel, source, prefixes);
    }

    public Task<List<StoredMonitored>> MonitoredAsync(CancellationToken cancel)
    {
        return ListAsync("SELECT prefix::text AS prefix, expected_origins, description FROM monitored_prefixes ORDER BY prefix",
            ReadMonitored, cancel);
    }

    public async Task<StoredMonitored?> MonitoredAsync(string prefix, CancellationToken cancel)
    {
        var rows = await ListAsync("SELECT prefix::text AS prefix, expected_origins, description FROM monitored_prefixes WHERE prefix = $1::cidr",
            ReadMonitored, cancel, prefix);
        return rows.FirstOrDefault();
    }

    /// <summary>Deletes the rows of a source; returns how many alerts and route updates went.</summary>
    public async Task<SourceDeletion> DeleteSourceAsync(string source, CancellationToken cancel)
    {
        await using var alerts = database.CreateCommand("DELETE FROM alerts WHERE source = $1");
        alerts.Parameters.Add(new NpgsqlParameter { Value = source });
        var alertCount = await alerts.ExecuteNonQueryAsync(cancel);
        await using var routes = database.CreateCommand("DELETE FROM route_updates WHERE source = $1");
        routes.Parameters.Add(new NpgsqlParameter { Value = source });
        return new SourceDeletion(alertCount, await routes.ExecuteNonQueryAsync(cancel));
    }

    private async Task<List<T>> ListAsync<T>(string sql, Func<DbDataReader, T> read, CancellationToken cancel, params object[] parameters)
    {
        await using var command = database.CreateCommand(sql);
        foreach (var value in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }
        await using var reader = await command.ExecuteReaderAsync(cancel);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancel))
        {
            rows.Add(read(reader));
        }
        return rows;
    }

    private static StoredAlert ReadAlert(DbDataReader r)
    {
        return new StoredAlert(r.GetInt64(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), NullableInt(r, 4),
            r.GetFieldValue<int[]>(5), NullableString(r, 6), r.GetString(7), r.GetFieldValue<int[]>(8), r.GetString(9), r.GetString(10));
    }

    private static StoredRoute ReadRoute(DbDataReader r)
    {
        return new StoredRoute(r.GetInt64(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), NullableInt(r, 4),
            r.GetFieldValue<int[]>(5), NullableString(r, 6), r.GetBoolean(7), r.GetString(8));
    }

    private static StoredMonitored ReadMonitored(DbDataReader r)
    {
        return new StoredMonitored(r.GetString(0), r.GetFieldValue<int[]>(1), r.GetString(2));
    }

    private static int? NullableInt(DbDataReader r, int column)
    {
        return r.IsDBNull(column) ? null : r.GetInt32(column);
    }

    private static string? NullableString(DbDataReader r, int column)
    {
        return r.IsDBNull(column) ? null : r.GetString(column);
    }
}

public sealed record Stats(Dictionary<string, long> ByType, long Alerts, long Routes, long Monitored);

public sealed record SourceDeletion(int Alerts, int Routes);
