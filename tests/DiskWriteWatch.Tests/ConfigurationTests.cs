using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData(60, 60)]
    [InlineData(60, 300)]
    [InlineData(300, 300)]
    public void ValidIntervalsAreAccepted(int bucket, int flush)
    {
        var options = Create(bucket, flush);
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData(60, 30)]
    [InlineData(60, 100)]
    [InlineData(5, 300)]
    [InlineData(60, 3601)]
    public void InvalidIntervalsAreRejected(int bucket, int flush)
    {
        var options = Create(bucket, flush);
        Assert.NotEmpty(options.Validate());
    }

    private static MonitorOptions Create(int bucket, int flush) => new()
    {
        Collection = new CollectionOptions { BucketSeconds = bucket },
        Storage = new StorageOptions { FlushIntervalSeconds = flush }
    };
}
