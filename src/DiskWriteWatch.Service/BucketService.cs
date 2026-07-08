namespace DiskWriteWatch.Service;

public sealed class BucketService(MonitorRuntime runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            runtime.CompleteElapsed(DateTimeOffset.UtcNow);
    }
}
