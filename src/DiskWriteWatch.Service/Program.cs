using System.Text.Json;
using DiskWriteWatch.Core;
using DiskWriteWatch.Service;
using Microsoft.Extensions.Hosting.WindowsServices;

var configPath = GetArgument(args, "--config")
    ?? Environment.GetEnvironmentVariable("DISKWRITEWATCH_CONFIG")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DiskWriteWatch", "config.json");
var serviceName = GetArgument(args, "--service-name") ?? "DiskWriteWatch";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);

var options = builder.Configuration.Get<MonitorOptions>() ?? new MonitorOptions();
options.ExpandEnvironmentVariables();
var validationErrors = options.Validate();
if (validationErrors.Count > 0)
{
    foreach (var error in validationErrors)
        Console.Error.WriteLine(error);
    return 2;
}
if (args.Contains("--validate-config", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"Configuration is valid. Data directory: {options.Storage.DataDirectory}");
    return 0;
}

builder.Host.UseWindowsService(service => service.ServiceName = serviceName);
builder.WebHost.UseUrls(options.Dashboard.ListenUrl);
builder.Logging.AddEventLog(settings => settings.SourceName = serviceName);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<MonitorRuntime>();
builder.Services.AddSingleton<ProcessRegistry>();
builder.Services.AddSingleton<DevicePathMapper>();
builder.Services.AddSingleton<DiskInventory>();
builder.Services.AddSingleton<SqliteStore>();
builder.Services.AddHostedService<PersistenceService>();
builder.Services.AddHostedService<BucketService>();
builder.Services.AddHostedService<EtwCollectorService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (MonitorRuntime runtime, SqliteStore store, DiskInventory inventory) =>
{
    var health = runtime.GetHealth();
    return Results.Ok(new
    {
        service = "DiskWriteWatch",
        version = typeof(Program).Assembly.GetName().Version?.ToString(),
        utcNow = DateTimeOffset.UtcNow,
        health.EtwActive,
        health.PendingBuckets,
        health.LastSuccessfulFlush,
        health.LastError,
        health.DroppedBuckets,
        health.BufferedEstimateBytes,
        health.CurrentBucketLostEvents,
        health.BucketSeconds,
        health.FlushIntervalSeconds,
        databaseBytes = store.GetDatabaseSizeBytes(),
        dataDirectory = options.Storage.DataDirectory,
        disks = inventory.GetDisks()
    });
});

app.MapGet("/api/timeline", async (HttpRequest request, MonitorRuntime runtime, SqliteStore store,
    DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var filter = ParseFilter(request, inventory);
    var persisted = await store.GetTimelineAsync(from, to, filter, cancellationToken);
    var all = TimelineMerger.Merge(persisted.Concat(runtime.GetLiveBuckets()
            .Where(x => x.StartUnixSeconds >= from && x.StartUnixSeconds <= to)
            .Select(x => new TimelinePoint(x.StartUnixSeconds,
                x.FileWrites.Where(filter.MatchesFile).Sum(y => y.Bytes) + x.OverflowFileBytes,
                x.DiskWrites.Where(filter.MatchesDisk).Sum(y => y.Bytes), false, false))));
    var logicalHistory = new Queue<long>();
    var result = all.Select(point =>
    {
        var anomaly = AnomalyDetector.IsAnomaly(point.LogicalBytes, logicalHistory, options.AnomalyDetection);
        logicalHistory.Enqueue(point.LogicalBytes);
        while (logicalHistory.Count > options.AnomalyDetection.BaselineMinutes)
            logicalHistory.Dequeue();
        return point with { Anomaly = anomaly };
    });
    return Results.Ok(result);
});

app.MapGet("/api/top/processes", async (HttpRequest request, MonitorRuntime runtime, SqliteStore store,
    DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var limit = ParseLimit(request);
    var includeMonitor = ParseBool(request.Query["includeMonitor"]);
    var filter = ParseFilter(request, inventory);
    var persisted = await store.GetTopProcessesAsync(from, to, limit * 2, includeMonitor, filter, cancellationToken);
    var live = runtime.GetLiveBuckets().Where(x => x.StartUnixSeconds >= from && x.StartUnixSeconds <= to)
        .SelectMany(x => x.FileWrites).Where(filter.MatchesFile)
        .Where(x => includeMonitor || !x.Process.Name.Equals("DiskWriteWatch", StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => x.Process.Name)
        .Select(x => new TopRow(x.Key, x.Sum(y => y.Bytes), x.Sum(y => y.Operations)));
    return Results.Ok(MergeTop(persisted, live, limit));
});

app.MapGet("/api/top/paths", async (HttpRequest request, MonitorRuntime runtime, SqliteStore store,
    DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var limit = ParseLimit(request);
    var includeMonitor = ParseBool(request.Query["includeMonitor"]);
    var filter = ParseFilter(request, inventory);
    var persisted = await store.GetTopPathsAsync(from, to, limit * 2, includeMonitor, filter, cancellationToken);
    var live = runtime.GetLiveBuckets().Where(x => x.StartUnixSeconds >= from && x.StartUnixSeconds <= to)
        .SelectMany(x => x.FileWrites).Where(filter.MatchesFile)
        .Where(x => includeMonitor || !x.Process.Name.Equals("DiskWriteWatch", StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => x.Path)
        .Select(x => new TopRow(x.Key, x.Sum(y => y.Bytes), x.Sum(y => y.Operations)));
    return Results.Ok(MergeTop(persisted, live, limit));
});

app.MapGet("/api/top/directories", async (HttpRequest request, MonitorRuntime runtime, SqliteStore store,
    DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var limit = ParseLimit(request);
    var includeMonitor = ParseBool(request.Query["includeMonitor"]);
    var filter = ParseFilter(request, inventory);
    var persisted = await store.GetTopDirectoriesAsync(from, to, limit * 2, includeMonitor, filter, cancellationToken);
    var live = runtime.GetLiveBuckets().Where(x => x.StartUnixSeconds >= from && x.StartUnixSeconds <= to)
        .SelectMany(x => x.FileWrites).Where(filter.MatchesFile)
        .Where(x => includeMonitor || !x.Process.Name.Equals("DiskWriteWatch", StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => x.Directory)
        .Select(x => new TopRow(x.Key, x.Sum(y => y.Bytes), x.Sum(y => y.Operations)));
    return Results.Ok(MergeTop(persisted, live, limit));
});

app.MapGet("/api/top/extensions", async (HttpRequest request, MonitorRuntime runtime, SqliteStore store,
    DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var limit = ParseLimit(request);
    var includeMonitor = ParseBool(request.Query["includeMonitor"]);
    var filter = ParseFilter(request, inventory);
    var persisted = await store.GetTopExtensionsAsync(from, to, limit * 2, includeMonitor, filter, cancellationToken);
    var live = runtime.GetLiveBuckets().Where(x => x.StartUnixSeconds >= from && x.StartUnixSeconds <= to)
        .SelectMany(x => x.FileWrites).Where(filter.MatchesFile)
        .Where(x => includeMonitor || !x.Process.Name.Equals("DiskWriteWatch", StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => string.IsNullOrEmpty(x.Extension) ? "<none>" : x.Extension)
        .Select(x => new TopRow(x.Key, x.Sum(y => y.Bytes), x.Sum(y => y.Operations)));
    return Results.Ok(MergeTop(persisted, live, limit));
});

app.MapGet("/api/export.csv", async (HttpRequest request, SqliteStore store, DiskInventory inventory,
    CancellationToken cancellationToken) =>
{
    var (from, to) = ParseRange(request);
    var csv = await store.ExportCsvAsync(from, to, ParseFilter(request, inventory), cancellationToken);
    return Results.Text(csv, "text/csv; charset=utf-8");
});

app.MapGet("/api/config", () => Results.Ok(new
{
    options.Collection.BucketSeconds,
    options.Storage.FlushIntervalSeconds,
    options.Dashboard.RefreshSeconds,
    options.Storage.MinuteRetentionDays,
    options.Storage.HourlyRetentionDays,
    options.AnomalyDetection,
    options.Privacy
}));

await app.RunAsync();
return 0;

static string? GetArgument(string[] values, string name)
{
    var index = Array.FindIndex(values, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static (long From, long To) ParseRange(HttpRequest request)
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var from = long.TryParse(request.Query["from"], out var parsedFrom) ? parsedFrom : now - 3600;
    var to = long.TryParse(request.Query["to"], out var parsedTo) ? parsedTo : now;
    var earliest = now - 366L * 86400;
    return (Math.Clamp(from, earliest, now), Math.Clamp(to, from, now + 60));
}

static int ParseLimit(HttpRequest request) =>
    int.TryParse(request.Query["limit"], out var value) ? Math.Clamp(value, 1, 1000) : 20;

static bool ParseBool(string? value) => bool.TryParse(value, out var result) && result;

static QueryFilter ParseFilter(HttpRequest request, DiskInventory inventory)
{
    int? disk = int.TryParse(request.Query["disk"], out var parsedDisk) && parsedDisk >= 0 ? parsedDisk : null;
    var diskVolumes = disk.HasValue ? inventory.GetVolumesForDisk(disk.Value) : null;
    return new QueryFilter(request.Query["process"].ToString(), request.Query["path"].ToString(),
        request.Query["volume"].ToString(), disk, diskVolumes);
}

static IReadOnlyList<TopRow> MergeTop(IEnumerable<TopRow> persisted, IEnumerable<TopRow> live, int limit) =>
    persisted.Concat(live).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .Select(x => new TopRow(x.Key, x.Sum(y => y.Bytes), x.Sum(y => y.Operations)))
        .OrderByDescending(x => x.Bytes).Take(limit).ToArray();
