using DiskWriteWatch.Core;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace DiskWriteWatch.Service;

public sealed class EtwCollectorService(
    MonitorRuntime runtime,
    ProcessRegistry processes,
    DevicePathMapper pathMapper,
    MonitorOptions options,
    ILogger<EtwCollectorService> logger) : BackgroundService
{
    private TraceEventSession? _session;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            runtime.SetEtwActive(false, "Kernel ETW collection is only supported on Windows.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                runtime.SetEtwActive(false, ex.Message);
                logger.LogError(ex, "Kernel ETW session failed; retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var sessionName = $"DiskWriteWatch-Kernel-{Environment.ProcessId}";
        using var session = new TraceEventSession(sessionName);
        _session = session;
        session.StopOnDispose = true;

        var keywords = KernelTraceEventParser.Keywords.Process;
        if (options.Collection.CollectFileIo)
            keywords |= KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO;
        if (options.Collection.CollectDiskIo)
            keywords |= KernelTraceEventParser.Keywords.DiskIO;

        session.EnableKernelProvider(keywords);
        var source = session.Source;

        source.Kernel.ProcessStart += data =>
        {
            var timestamp = ToUnixSeconds(data.TimeStamp);
            var process = processes.Register(data.ProcessID, timestamp, data.ProcessName,
                data.ImageFileName, data.CommandLine);
            runtime.Aggregator.RegisterProcess(process);
        };
        source.Kernel.ProcessStop += data =>
        {
            var timestamp = ToUnixSeconds(data.TimeStamp);
            var stopped = processes.Stop(data.ProcessID, timestamp);
            if (stopped is not null)
                runtime.Aggregator.StopProcess(stopped.Key, timestamp);
        };
        if (options.Collection.CollectFileIo)
        {
            source.Kernel.FileIOWrite += data =>
            {
                // FileIO also reports cache-manager paging writes for data already counted at the
                // application boundary. Those events carry the kernel paging mask and would double-count
                // logical I/O; physical traffic remains available from DiskIOWrite.
                const int kernelPagingMask = 0x60000;
                if ((data.IoFlags & kernelPagingMask) == kernelPagingMask)
                    return;
                var process = processes.Resolve(data.ProcessID);
                var path = pathMapper.Map(data.FileName ?? "<unknown>");
                if (!options.Privacy.StoreFullPaths)
                    path = PathHelpers.Describe(path).Directory;
                runtime.Aggregator.AddFileWrite(process, path, data.IoSize, ToUnixSeconds(data.TimeStamp));
            };
        }
        if (options.Collection.CollectDiskIo)
        {
            source.Kernel.DiskIOWrite += data =>
            {
                var process = processes.Resolve(data.ProcessID);
                runtime.Aggregator.AddDiskWrite(process, data.DiskNumber, data.TransferSize,
                    ToUnixSeconds(data.TimeStamp));
            };
        }

        runtime.SetEtwActive(true);
        logger.LogInformation("Kernel ETW session {SessionName} started with keywords {Keywords}.", sessionName, keywords);

        using var registration = cancellationToken.Register(() => source.StopProcessing());
        var processing = Task.Run(source.Process, CancellationToken.None);
        long observedLost = 0;
        while (!processing.IsCompleted)
        {
            await Task.WhenAny(processing, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            var currentLost = session.EventsLost;
            if (currentLost > observedLost)
            {
                runtime.Aggregator.AddLostEvents(currentLost - observedLost);
                observedLost = currentLost;
            }
        }
        await processing;
        runtime.SetEtwActive(false);
        _session = null;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _session?.Source.StopProcessing(); }
        catch { /* best-effort shutdown */ }
        return base.StopAsync(cancellationToken);
    }

    private static long ToUnixSeconds(DateTime timestamp) =>
        new DateTimeOffset(timestamp.ToUniversalTime()).ToUnixTimeSeconds();
}
