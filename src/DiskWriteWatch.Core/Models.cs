namespace DiskWriteWatch.Core;

public readonly record struct ProcessKey(int Pid, long StartUnixSeconds);

public sealed record ProcessInfo(
    ProcessKey Key,
    string Name,
    string ImagePath,
    string Role,
    long? EndUnixSeconds = null);

public readonly record struct FileWriteKey(ProcessKey Process, string Path);
public readonly record struct DiskWriteKey(ProcessKey Process, int DiskNumber);

public sealed record FileWriteAggregate(
    ProcessInfo Process,
    string Path,
    string Directory,
    string Extension,
    long Bytes,
    long Operations);

public sealed record DiskWriteAggregate(
    ProcessInfo Process,
    int DiskNumber,
    long Bytes,
    long Operations);

public sealed record BucketSnapshot(
    long StartUnixSeconds,
    int DurationSeconds,
    IReadOnlyList<FileWriteAggregate> FileWrites,
    IReadOnlyList<DiskWriteAggregate> DiskWrites,
    long OverflowFileBytes,
    long OverflowFileOperations,
    long LostEvents,
    bool Persisted = false)
{
    public long EndUnixSeconds => StartUnixSeconds + DurationSeconds;
    public long TotalLogicalBytes => FileWrites.Sum(x => x.Bytes) + OverflowFileBytes;
    public long TotalPhysicalBytes => DiskWrites.Sum(x => x.Bytes);
}

public sealed record TimelinePoint(
    long BucketUnixSeconds,
    long LogicalBytes,
    long PhysicalBytes,
    bool Persisted,
    bool Anomaly);

public sealed record TopRow(string Name, long Bytes, long Operations);

public sealed record QueryFilter(
    string Process = "",
    string Path = "",
    string Volume = "",
    int? Disk = null,
    IReadOnlyList<string>? DiskVolumes = null)
{
    public bool MatchesFile(FileWriteAggregate row) =>
        Contains(row.Process.Name, Process) && Contains(row.Path, Path) &&
        (string.IsNullOrWhiteSpace(Volume) || StartsWithVolume(row.Path, Volume)) &&
        (!Disk.HasValue || DiskVolumes?.Any(volume => StartsWithVolume(row.Path, volume)) == true);

    public bool MatchesDisk(DiskWriteAggregate row) =>
        Contains(row.Process.Name, Process) && (!Disk.HasValue || row.DiskNumber == Disk.Value);

    private static bool Contains(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithVolume(string path, string volume) =>
        path.StartsWith(volume.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}

internal sealed class MutableCounter
{
    public long Bytes;
    public long Operations;
}
