using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class AggregationTests
{
    [Fact]
    public void AggregatesFileAndDiskWrites()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var engine = new AggregationEngine(60, 1000, start);
        var process = new ProcessInfo(new ProcessKey(42, start.ToUnixTimeSeconds()), "writer", @"C:\writer.exe", "process");

        engine.AddFileWrite(process, @"C:\temp\payload.bin", 1024, start.ToUnixTimeSeconds());
        engine.AddFileWrite(process, @"C:\temp\payload.bin", 2048, start.ToUnixTimeSeconds() + 1);
        engine.AddDiskWrite(process, 0, 4096, start.ToUnixTimeSeconds() + 1);

        var bucket = Assert.Single(engine.CompleteElapsed(start.AddSeconds(61)));
        var file = Assert.Single(bucket.FileWrites);
        var disk = Assert.Single(bucket.DiskWrites);
        Assert.Equal(3072, file.Bytes);
        Assert.Equal(2, file.Operations);
        Assert.Equal(4096, disk.Bytes);
        Assert.Equal(0, disk.DiskNumber);
    }

    [Fact]
    public void OverflowIsCountedInsteadOfGrowingWithoutBound()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var engine = new AggregationEngine(60, 2, start);
        var process = new ProcessInfo(new ProcessKey(1, 1), "writer", string.Empty, "process");
        engine.AddFileWrite(process, "a", 1, start.ToUnixTimeSeconds());
        engine.AddFileWrite(process, "b", 2, start.ToUnixTimeSeconds());
        engine.AddFileWrite(process, "c", 3, start.ToUnixTimeSeconds());
        var bucket = engine.GetCurrentSnapshot();
        Assert.Equal(2, bucket.FileWrites.Count);
        Assert.Equal(3, bucket.OverflowFileBytes);
    }

    [Fact]
    public void ProcessKeyProtectsAgainstPidReuse()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var engine = new AggregationEngine(60, 100, start);
        var oldProcess = new ProcessInfo(new ProcessKey(7, 100), "old", string.Empty, "process");
        var newProcess = new ProcessInfo(new ProcessKey(7, 200), "new", string.Empty, "process");
        engine.AddFileWrite(oldProcess, "a", 10, start.ToUnixTimeSeconds());
        engine.AddFileWrite(newProcess, "a", 20, start.ToUnixTimeSeconds());
        Assert.Equal(2, engine.GetCurrentSnapshot().FileWrites.Count);
    }
}
