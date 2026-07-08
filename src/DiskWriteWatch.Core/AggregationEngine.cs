namespace DiskWriteWatch.Core;

public sealed class AggregationEngine
{
    private const int MaxBucketsPerCompletion = 10_000;
    private readonly object _gate = new();
    private readonly int _bucketSeconds;
    private readonly int _maxKeys;
    private long _bucketStart;
    private readonly Dictionary<FileWriteKey, MutableCounter> _files = [];
    private readonly Dictionary<DiskWriteKey, MutableCounter> _disks = [];
    private readonly Dictionary<ProcessKey, ProcessInfo> _processes = [];
    private long _overflowBytes;
    private long _overflowOperations;
    private long _lostEvents;

    public AggregationEngine(int bucketSeconds, int maxKeys, DateTimeOffset? now = null)
    {
        _bucketSeconds = bucketSeconds;
        _maxKeys = maxKeys;
        _bucketStart = Align((now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());
    }

    public void RegisterProcess(ProcessInfo process)
    {
        lock (_gate)
            _processes[process.Key] = process;
    }

    public void StopProcess(ProcessKey key, long endUnixSeconds)
    {
        lock (_gate)
        {
            if (_processes.TryGetValue(key, out var process))
                _processes[key] = process with { EndUnixSeconds = endUnixSeconds };
        }
    }

    public void AddFileWrite(ProcessInfo process, string path, long bytes, long timestampUnixSeconds)
    {
        if (bytes <= 0)
            return;
        lock (_gate)
        {
            _processes[process.Key] = process;
            if (timestampUnixSeconds >= _bucketStart + _bucketSeconds)
                return;
            var key = new FileWriteKey(process.Key, path);
            if (!_files.TryGetValue(key, out var counter))
            {
                if (_files.Count >= _maxKeys)
                {
                    _overflowBytes += bytes;
                    _overflowOperations++;
                    return;
                }
                counter = new MutableCounter();
                _files[key] = counter;
            }
            counter.Bytes += bytes;
            counter.Operations++;
        }
    }

    public void AddDiskWrite(ProcessInfo process, int diskNumber, long bytes, long timestampUnixSeconds)
    {
        if (bytes <= 0)
            return;
        lock (_gate)
        {
            _processes[process.Key] = process;
            if (timestampUnixSeconds >= _bucketStart + _bucketSeconds)
                return;
            var key = new DiskWriteKey(process.Key, diskNumber);
            if (!_disks.TryGetValue(key, out var counter))
            {
                counter = new MutableCounter();
                _disks[key] = counter;
            }
            counter.Bytes += bytes;
            counter.Operations++;
        }
    }

    public void AddLostEvents(long count)
    {
        lock (_gate)
            _lostEvents += Math.Max(0, count);
    }

    public IReadOnlyList<BucketSnapshot> CompleteElapsed(DateTimeOffset now)
    {
        lock (_gate)
        {
            var nowUnix = now.ToUnixTimeSeconds();
            if (nowUnix < _bucketStart + _bucketSeconds)
                return [];
            var completed = new List<BucketSnapshot>();
            completed.Add(BuildSnapshot());
            ClearCounters();
            _bucketStart += _bucketSeconds;
            var elapsedBuckets = (nowUnix - _bucketStart) / _bucketSeconds;
            if (elapsedBuckets >= MaxBucketsPerCompletion)
                _bucketStart += (elapsedBuckets - MaxBucketsPerCompletion + 1) * _bucketSeconds;
            while (nowUnix >= _bucketStart + _bucketSeconds)
            {
                completed.Add(new BucketSnapshot(_bucketStart, _bucketSeconds, [], [], 0, 0, 0));
                _bucketStart += _bucketSeconds;
            }
            return completed;
        }
    }

    public BucketSnapshot GetCurrentSnapshot()
    {
        lock (_gate)
            return BuildSnapshot();
    }

    private BucketSnapshot BuildSnapshot()
    {
        var fileRows = _files.Select(pair =>
        {
            var info = _processes.GetValueOrDefault(pair.Key.Process) ?? UnknownProcess(pair.Key.Process);
            var description = PathHelpers.Describe(pair.Key.Path);
            return new FileWriteAggregate(info, pair.Key.Path, description.Directory, description.Extension,
                pair.Value.Bytes, pair.Value.Operations);
        }).ToArray();
        var diskRows = _disks.Select(pair =>
        {
            var info = _processes.GetValueOrDefault(pair.Key.Process) ?? UnknownProcess(pair.Key.Process);
            return new DiskWriteAggregate(info, pair.Key.DiskNumber, pair.Value.Bytes, pair.Value.Operations);
        }).ToArray();
        return new BucketSnapshot(_bucketStart, _bucketSeconds, fileRows, diskRows,
            _overflowBytes, _overflowOperations, _lostEvents);
    }

    private void ClearCounters()
    {
        _files.Clear();
        _disks.Clear();
        _overflowBytes = 0;
        _overflowOperations = 0;
        _lostEvents = 0;
    }

    private long Align(long unixSeconds) => unixSeconds - unixSeconds % _bucketSeconds;

    private static ProcessInfo UnknownProcess(ProcessKey key) =>
        new(key, key.Pid == 4 ? "System" : $"pid-{key.Pid}", string.Empty, "unknown");
}
