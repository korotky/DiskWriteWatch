using DiskWriteWatch.Core;

namespace DiskWriteWatch.Service;

public sealed class PersistenceService(
    MonitorRuntime runtime,
    SqliteStore store,
    MonitorOptions options,
    ILogger<PersistenceService> logger) : BackgroundService
{
    private DateTimeOffset _lastMaintenance = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.InitializeAsync(stoppingToken);
                await store.MaintainAsync(stoppingToken);
                _lastMaintenance = DateTimeOffset.UtcNow;
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Storage initialization failed; retrying in 30 seconds.");
                runtime.SetEtwActive(runtime.GetHealth().EtwActive, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Storage.FlushIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await FlushAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        runtime.CompleteElapsed(DateTimeOffset.UtcNow.AddSeconds(options.Collection.BucketSeconds));
        await FlushAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var buckets = runtime.TakePending();
        if (buckets.Count == 0)
            return;
        try
        {
            await store.SaveBucketsAsync(buckets, cancellationToken);
            runtime.MarkFlushSucceeded(DateTimeOffset.UtcNow);
            if (DateTimeOffset.UtcNow - _lastMaintenance >= TimeSpan.FromDays(1))
            {
                await store.MaintainAsync(cancellationToken);
                _lastMaintenance = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            runtime.Requeue(buckets, ex);
            logger.LogError(ex, "Failed to persist {BucketCount} buckets.", buckets.Count);
        }
    }
}
