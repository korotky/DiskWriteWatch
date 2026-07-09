using System.Globalization;
using System.Text;
using DiskWriteWatch.Core;
using Microsoft.Data.Sqlite;

namespace DiskWriteWatch.Service;

public sealed class SqliteStore(MonitorOptions options, DiskInventory inventory, ILogger<SqliteStore> logger)
{
    private string DatabasePath => Path.Combine(options.Storage.DataDirectory, "monitor.db");
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Storage.DataDirectory);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        logger.LogInformation("Using SQLite database {DatabasePath}", DatabasePath);
        var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveBucketsAsync(IReadOnlyList<BucketSnapshot> buckets, CancellationToken cancellationToken)
    {
        if (buckets.Count == 0)
            return;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (var bucket in buckets)
        {
            foreach (var row in bucket.FileWrites)
                await InsertFileAsync(connection, transaction, bucket, row, cancellationToken);
            if (bucket.OverflowFileBytes > 0)
            {
                var overflowProcess = new ProcessInfo(new ProcessKey(-1, 0), "<overflow>", string.Empty, "overflow");
                await InsertFileAsync(connection, transaction, bucket,
                    new FileWriteAggregate(overflowProcess, "<other>", "<other>", string.Empty,
                        bucket.OverflowFileBytes, bucket.OverflowFileOperations), cancellationToken);
            }
            foreach (var row in bucket.DiskWrites)
                await InsertDiskAsync(connection, transaction, bucket, row, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TimelinePoint>> GetTimelineAsync(long fromUnix, long toUnix, QueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = new List<TimelinePoint>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_utc, SUM(logical_bytes), SUM(physical_bytes)
            FROM (
                SELECT bucket_utc, bytes AS logical_bytes, 0 AS physical_bytes
                FROM file_writes WHERE bucket_utc BETWEEN $from AND $to
                  AND ($process = '' OR process_name LIKE '%' || $process || '%')
                  AND ($path = '' OR path LIKE '%' || $path || '%')
                  AND ($volume = '' OR path LIKE $volume || '\\%')
                  AND ($disk < 0 OR ddw_path_matches_volumes(path, $diskVolumes) = 1)
                UNION ALL
                SELECT bucket_utc, 0 AS logical_bytes, bytes AS physical_bytes
                FROM disk_writes WHERE bucket_utc BETWEEN $from AND $to
                  AND ($process = '' OR process_name LIKE '%' || $process || '%')
                  AND ($disk < 0 OR disk_number = $disk)
                UNION ALL
                SELECT hour_utc, bytes, 0 FROM hourly_file_writes
                WHERE hour_utc BETWEEN $from AND $to
                  AND ($process = '' OR process_name LIKE '%' || $process || '%')
                  AND ($path = '' OR path LIKE '%' || $path || '%')
                  AND ($volume = '' OR path LIKE $volume || '\\%')
                  AND ($disk < 0 OR ddw_path_matches_volumes(path, $diskVolumes) = 1)
                UNION ALL
                SELECT hour_utc, 0, bytes FROM hourly_disk_writes
                WHERE hour_utc BETWEEN $from AND $to
                  AND ($process = '' OR process_name LIKE '%' || $process || '%')
                  AND ($disk < 0 OR disk_number = $disk)
            ) GROUP BY bucket_utc ORDER BY bucket_utc;
            """;
        command.Parameters.AddWithValue("$from", fromUnix);
        command.Parameters.AddWithValue("$to", toUnix);
        AddFilterParameters(command, filter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TimelinePoint(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), true, false));
        return result;
    }

    public Task<IReadOnlyList<TopRow>> GetTopProcessesAsync(long fromUnix, long toUnix, int limit,
        bool includeMonitor, QueryFilter filter, CancellationToken cancellationToken) =>
        GetTopAsync("process_name", fromUnix, toUnix, limit, includeMonitor, filter, cancellationToken);

    public Task<IReadOnlyList<TopRow>> GetTopPathsAsync(long fromUnix, long toUnix, int limit,
        bool includeMonitor, QueryFilter filter, CancellationToken cancellationToken) =>
        GetTopAsync("path", fromUnix, toUnix, limit, includeMonitor, filter, cancellationToken);

    public Task<IReadOnlyList<TopRow>> GetTopDirectoriesAsync(long fromUnix, long toUnix, int limit,
        bool includeMonitor, QueryFilter filter, CancellationToken cancellationToken) =>
        GetTopAsync("directory", fromUnix, toUnix, limit, includeMonitor, filter, cancellationToken);

    public Task<IReadOnlyList<TopRow>> GetTopExtensionsAsync(long fromUnix, long toUnix, int limit,
        bool includeMonitor, QueryFilter filter, CancellationToken cancellationToken) =>
        GetTopAsync("extension", fromUnix, toUnix, limit, includeMonitor, filter, cancellationToken);

    public async Task<string> ExportCsvAsync(long fromUnix, long toUnix, QueryFilter filter,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder("bucket_utc,process,pid,path,bytes,operations\r\n");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_utc, process_name, pid, path, bytes, operations
            FROM file_writes WHERE bucket_utc BETWEEN $from AND $to
              AND ($process = '' OR process_name LIKE '%' || $process || '%')
              AND ($path = '' OR path LIKE '%' || $path || '%')
              AND ($volume = '' OR path LIKE $volume || '\\%')
              AND ($disk < 0 OR ddw_path_matches_volumes(path, $diskVolumes) = 1)
            ORDER BY bucket_utc, bytes DESC;
            """;
        command.Parameters.AddWithValue("$from", fromUnix);
        command.Parameters.AddWithValue("$to", toUnix);
        AddFilterParameters(command, filter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            output.Append(reader.GetInt64(0)).Append(',')
                .Append(Csv(reader.GetString(1))).Append(',')
                .Append(reader.GetInt32(2)).Append(',')
                .Append(Csv(reader.GetString(3))).Append(',')
                .Append(reader.GetInt64(4)).Append(',')
                .Append(reader.GetInt64(5)).Append("\r\n");
        }
        return output.ToString();
    }

    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        var minuteCutoff = DateTimeOffset.UtcNow.AddDays(-options.Storage.MinuteRetentionDays).ToUnixTimeSeconds();
        var hourlyCutoff = DateTimeOffset.UtcNow.AddDays(-options.Storage.HourlyRetentionDays).ToUnixTimeSeconds();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO hourly_file_writes
            SELECT (bucket_utc / 3600) * 3600, process_name, image_path, role, path, directory, extension,
                   SUM(bytes), SUM(operations)
            FROM file_writes WHERE bucket_utc < $minuteCutoff
            GROUP BY (bucket_utc / 3600), process_name, image_path, role, path, directory, extension;
            INSERT INTO hourly_disk_writes
            SELECT (bucket_utc / 3600) * 3600, process_name, image_path, role, disk_number,
                   SUM(bytes), SUM(operations)
            FROM disk_writes WHERE bucket_utc < $minuteCutoff
            GROUP BY (bucket_utc / 3600), process_name, image_path, role, disk_number;
            DELETE FROM file_writes WHERE bucket_utc < $minuteCutoff;
            DELETE FROM disk_writes WHERE bucket_utc < $minuteCutoff;
            DELETE FROM hourly_file_writes WHERE hour_utc < $hourlyCutoff;
            DELETE FROM hourly_disk_writes WHERE hour_utc < $hourlyCutoff;
            """;
        command.Parameters.AddWithValue("$minuteCutoff", minuteCutoff);
        command.Parameters.AddWithValue("$hourlyCutoff", hourlyCutoff);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var vacuum = connection.CreateCommand();
        vacuum.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(2000);";
        await vacuum.ExecuteNonQueryAsync(cancellationToken);
        await EnforceMaximumSizeAsync(connection, cancellationToken);
    }

    public long GetDatabaseSizeBytes()
    {
        try
        {
            return new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" }
                .Where(File.Exists).Sum(path => new FileInfo(path).Length);
        }
        catch { return 0; }
    }

    private async Task<IReadOnlyList<TopRow>> GetTopAsync(string column, long fromUnix, long toUnix,
        int limit, bool includeMonitor, QueryFilter filter, CancellationToken cancellationToken)
    {
        if (column is not ("process_name" or "path" or "directory" or "extension"))
            throw new ArgumentOutOfRangeException(nameof(column));
        var result = new List<TopRow>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {column}, SUM(bytes), SUM(operations)
            FROM (
              SELECT process_name, path, directory, extension, bytes, operations, bucket_utc FROM file_writes
              UNION ALL
              SELECT process_name, path, directory, extension, bytes, operations, hour_utc FROM hourly_file_writes
            )
            WHERE bucket_utc BETWEEN $from AND $to
              AND ($includeMonitor = 1 OR process_name <> 'DiskWriteWatch')
              AND ($process = '' OR process_name LIKE '%' || $process || '%')
              AND ($path = '' OR path LIKE '%' || $path || '%')
              AND ($volume = '' OR path LIKE $volume || '\\%')
              AND ($disk < 0 OR ddw_path_matches_volumes(path, $diskVolumes) = 1)
            GROUP BY {column} ORDER BY SUM(bytes) DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", fromUnix);
        command.Parameters.AddWithValue("$to", toUnix);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        command.Parameters.AddWithValue("$includeMonitor", includeMonitor ? 1 : 0);
        AddFilterParameters(command, filter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TopRow(reader.IsDBNull(0) ? "<unknown>" : reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return result;
    }

    private static async Task InsertFileAsync(SqliteConnection connection, SqliteTransaction transaction,
        BucketSnapshot bucket, FileWriteAggregate row, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO file_writes VALUES
            ($bucket,$duration,$pid,$start,$name,$image,$role,$path,$directory,$extension,$bytes,$operations,$persisted);
            """;
        AddProcessParameters(command, row.Process);
        command.Parameters.AddWithValue("$bucket", bucket.StartUnixSeconds);
        command.Parameters.AddWithValue("$duration", bucket.DurationSeconds);
        command.Parameters.AddWithValue("$path", row.Path);
        command.Parameters.AddWithValue("$directory", row.Directory);
        command.Parameters.AddWithValue("$extension", row.Extension);
        command.Parameters.AddWithValue("$bytes", row.Bytes);
        command.Parameters.AddWithValue("$operations", row.Operations);
        command.Parameters.AddWithValue("$persisted", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDiskAsync(SqliteConnection connection, SqliteTransaction transaction,
        BucketSnapshot bucket, DiskWriteAggregate row, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO disk_writes VALUES
            ($bucket,$duration,$pid,$start,$name,$image,$role,$disk,$bytes,$operations,$persisted);
            """;
        AddProcessParameters(command, row.Process);
        command.Parameters.AddWithValue("$bucket", bucket.StartUnixSeconds);
        command.Parameters.AddWithValue("$duration", bucket.DurationSeconds);
        command.Parameters.AddWithValue("$disk", row.DiskNumber);
        command.Parameters.AddWithValue("$bytes", row.Bytes);
        command.Parameters.AddWithValue("$operations", row.Operations);
        command.Parameters.AddWithValue("$persisted", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProcessParameters(SqliteCommand command, ProcessInfo process)
    {
        command.Parameters.AddWithValue("$pid", process.Key.Pid);
        command.Parameters.AddWithValue("$start", process.Key.StartUnixSeconds);
        command.Parameters.AddWithValue("$name", process.Name);
        command.Parameters.AddWithValue("$image", process.ImagePath);
        command.Parameters.AddWithValue("$role", process.Role);
    }

    private void AddFilterParameters(SqliteCommand command, QueryFilter filter)
    {
        command.Parameters.AddWithValue("$process", filter.Process?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$path", filter.Path?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$volume", filter.Volume?.Trim().TrimEnd('\\') ?? string.Empty);
        command.Parameters.AddWithValue("$disk", filter.Disk ?? -1);
        command.Parameters.AddWithValue("$diskVolumes", inventory.GetVolumeFilterText(filter.DiskVolumes));
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        connection.CreateFunction("ddw_path_matches_volumes",
            (string? path, string? volumes) => DiskInventory.PathBelongsToAnyVolume(path, volumes) ? 1 : 0);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA temp_store=MEMORY;
            PRAGMA cache_size=-65536;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private async Task EnforceMaximumSizeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var maximumBytes = options.Storage.MaxDatabaseSizeMb * 1024L * 1024L;
        for (var pass = 0; pass < 20 && GetDatabaseSizeBytes() > maximumBytes; pass++)
        {
            var prune = connection.CreateCommand();
            prune.CommandText = """
                DELETE FROM hourly_file_writes WHERE rowid IN
                  (SELECT rowid FROM hourly_file_writes ORDER BY hour_utc LIMIT 50000);
                DELETE FROM hourly_disk_writes WHERE rowid IN
                  (SELECT rowid FROM hourly_disk_writes ORDER BY hour_utc LIMIT 50000);
                DELETE FROM file_writes WHERE rowid IN
                  (SELECT rowid FROM file_writes ORDER BY bucket_utc LIMIT 50000);
                DELETE FROM disk_writes WHERE rowid IN
                  (SELECT rowid FROM disk_writes ORDER BY bucket_utc LIMIT 50000);
                """;
            var deleted = await prune.ExecuteNonQueryAsync(cancellationToken);
            if (deleted == 0)
                break;
            var reclaim = connection.CreateCommand();
            reclaim.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(5000);";
            await reclaim.ExecuteNonQueryAsync(cancellationToken);
        }
        if (GetDatabaseSizeBytes() > maximumBytes)
            logger.LogWarning("Database remains above configured size limit of {MaximumSizeMb} MiB after pruning.",
                options.Storage.MaxDatabaseSizeMb);
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    private const string Schema = """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        PRAGMA auto_vacuum=INCREMENTAL;
        CREATE TABLE IF NOT EXISTS file_writes (
          bucket_utc INTEGER NOT NULL, duration_seconds INTEGER NOT NULL,
          pid INTEGER NOT NULL, process_start_utc INTEGER NOT NULL,
          process_name TEXT NOT NULL, image_path TEXT NOT NULL, role TEXT NOT NULL,
          path TEXT NOT NULL, directory TEXT NOT NULL, extension TEXT NOT NULL,
          bytes INTEGER NOT NULL, operations INTEGER NOT NULL, persisted_utc INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS disk_writes (
          bucket_utc INTEGER NOT NULL, duration_seconds INTEGER NOT NULL,
          pid INTEGER NOT NULL, process_start_utc INTEGER NOT NULL,
          process_name TEXT NOT NULL, image_path TEXT NOT NULL, role TEXT NOT NULL,
          disk_number INTEGER NOT NULL, bytes INTEGER NOT NULL, operations INTEGER NOT NULL,
          persisted_utc INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS hourly_file_writes (
          hour_utc INTEGER NOT NULL, process_name TEXT NOT NULL, image_path TEXT NOT NULL,
          role TEXT NOT NULL, path TEXT NOT NULL, directory TEXT NOT NULL, extension TEXT NOT NULL,
          bytes INTEGER NOT NULL, operations INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS hourly_disk_writes (
          hour_utc INTEGER NOT NULL, process_name TEXT NOT NULL, image_path TEXT NOT NULL,
          role TEXT NOT NULL, disk_number INTEGER NOT NULL, bytes INTEGER NOT NULL, operations INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_file_bucket ON file_writes(bucket_utc);
        CREATE INDEX IF NOT EXISTS ix_file_process ON file_writes(process_name, bucket_utc);
        CREATE INDEX IF NOT EXISTS ix_file_path ON file_writes(path, bucket_utc);
        CREATE INDEX IF NOT EXISTS ix_disk_bucket ON disk_writes(bucket_utc);
        CREATE INDEX IF NOT EXISTS ix_hour_file ON hourly_file_writes(hour_utc);
        CREATE INDEX IF NOT EXISTS ix_hour_disk ON hourly_disk_writes(hour_utc);
        """;
}
