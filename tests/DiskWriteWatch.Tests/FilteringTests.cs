using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class FilteringTests
{
    private static readonly ProcessInfo Chrome = new(new ProcessKey(10, 100), "chrome.exe", @"C:\Chrome\chrome.exe", "browser");

    [Fact]
    public void FileFiltersAreCaseInsensitiveAndComposable()
    {
        var row = new FileWriteAggregate(Chrome, @"C:\Cache\file.tmp", @"C:\Cache", ".tmp", 10, 1);
        Assert.True(new QueryFilter("CHROME", "file", "c:").MatchesFile(row));
        Assert.False(new QueryFilter(Path: "other").MatchesFile(row));
        Assert.False(new QueryFilter(Volume: "E:").MatchesFile(row));
    }

    [Fact]
    public void DiskFilterSelectsPhysicalDisk()
    {
        var row = new DiskWriteAggregate(Chrome, 1, 10, 1);
        Assert.True(new QueryFilter(Process: "chrome", Disk: 1).MatchesDisk(row));
        Assert.False(new QueryFilter(Disk: 0).MatchesDisk(row));
    }
}
