using System.Collections.Concurrent;
using System.Diagnostics;
using DiskWriteWatch.Core;

namespace DiskWriteWatch.Service;

public sealed class ProcessRegistry
{
    private readonly ConcurrentDictionary<int, ProcessInfo> _processes = new();

    public ProcessRegistry()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
                _processes[process.Id] = new ProcessInfo(new ProcessKey(process.Id, start),
                    process.ProcessName, TryGetImagePath(process), "process");
            }
            catch
            {
                // A protected or terminating process is resolved lazily later.
            }
            finally
            {
                process.Dispose();
            }
        }
        _processes[4] = new ProcessInfo(new ProcessKey(4, 0), "System", string.Empty, "system");
    }

    public ProcessInfo Register(int pid, long startUnixSeconds, string? name, string? imagePath, string? commandLine)
    {
        var info = new ProcessInfo(new ProcessKey(pid, startUnixSeconds),
            string.IsNullOrWhiteSpace(name) ? $"pid-{pid}" : name,
            imagePath ?? string.Empty,
            PathHelpers.GetProcessRole(commandLine));
        _processes[pid] = info;
        return info;
    }

    public ProcessInfo Resolve(int pid)
    {
        if (_processes.TryGetValue(pid, out var result))
            return result;
        try
        {
            using var process = Process.GetProcessById(pid);
            var start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
            result = new ProcessInfo(new ProcessKey(pid, start), process.ProcessName,
                TryGetImagePath(process), "process");
        }
        catch
        {
            result = new ProcessInfo(new ProcessKey(pid, 0), pid == 4 ? "System" : $"pid-{pid}",
                string.Empty, "unknown");
        }
        _processes[pid] = result;
        return result;
    }

    public ProcessInfo? Stop(int pid, long endUnixSeconds)
    {
        if (!_processes.TryRemove(pid, out var process))
            return null;
        return process with { EndUnixSeconds = endUnixSeconds };
    }

    private static string TryGetImagePath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }
}
