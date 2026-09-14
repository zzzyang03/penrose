using Microsoft.Data.Sqlite;

namespace Penrose.Persistence;

public sealed record PlaybackProgressRecord(
    string Uri,
    long PositionMs,
    long? DurationMs,
    DateTimeOffset UpdatedAt);

public sealed class PlaybackStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlaybackStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".");
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                  version INTEGER PRIMARY KEY
                );
                CREATE TABLE IF NOT EXISTS playback_progress (
                  uri TEXT PRIMARY KEY,
                  position_ms INTEGER NOT NULL,
                  duration_ms INTEGER,
                  updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS settings (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                );
                INSERT OR IGNORE INTO schema_migrations(version) VALUES (1);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task UpsertProgressAsync(PlaybackProgressRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return WithLockAsync(async () =>
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO playback_progress(uri, position_ms, duration_ms, updated_at)
                VALUES ($uri, $position, $duration, $updated)
                ON CONFLICT(uri) DO UPDATE SET
                  position_ms = excluded.position_ms,
                  duration_ms = excluded.duration_ms,
                  updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$uri", record.Uri);
            command.Parameters.AddWithValue("$position", record.PositionMs);
            command.Parameters.AddWithValue("$duration", (object?)record.DurationMs ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<PlaybackProgressRecord?> GetProgressAsync(string uri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        return WithLockAsync(async () =>
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                SELECT uri, position_ms, duration_ms, updated_at
                FROM playback_progress
                WHERE uri = $uri;
                """;
            command.Parameters.AddWithValue("$uri", uri);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new PlaybackProgressRecord(
                reader.GetString(0),
                reader.GetInt64(1),
                await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetInt64(2),
                DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind));
        }, cancellationToken);
    }

    /// <summary>Most recently updated progress rows, newest first.</summary>
    public Task<IReadOnlyList<PlaybackProgressRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        return WithLockAsync<IReadOnlyList<PlaybackProgressRecord>>(async () =>
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                SELECT uri, position_ms, duration_ms, updated_at
                FROM playback_progress
                ORDER BY updated_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            List<PlaybackProgressRecord> rows = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!DateTimeOffset.TryParse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset updated))
                {
                    continue;
                }

                rows.Add(new PlaybackProgressRecord(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetInt64(2),
                    updated));
            }

            return rows;
        }, cancellationToken);
    }

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        return WithLockAsync(async () =>
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO settings(key, value)
                VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return WithLockAsync(async () =>
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return scalar is string text ? text : null;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task WithLockAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
