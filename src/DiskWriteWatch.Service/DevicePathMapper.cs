using System.Runtime.InteropServices;
using System.Text;

namespace DiskWriteWatch.Service;

public sealed class DevicePathMapper
{
    private readonly object _gate = new();
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private IReadOnlyList<(string Device, string Drive)> _mappings = [];

    public string Map(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            return path;
        RefreshIfNeeded();
        foreach (var mapping in _mappings)
        {
            if (path.StartsWith(mapping.Device, StringComparison.OrdinalIgnoreCase))
                return mapping.Drive + path[mapping.Device.Length..];
        }
        return path;
    }

    private void RefreshIfNeeded()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMinutes(5))
                return;
            var mappings = new List<(string Device, string Drive)>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                var name = drive.Name.TrimEnd('\\');
                var buffer = new StringBuilder(1024);
                if (QueryDosDevice(name, buffer, buffer.Capacity) != 0)
                    mappings.Add((buffer.ToString(), name));
            }
            _mappings = mappings.OrderByDescending(x => x.Device.Length).ToArray();
            _lastRefresh = DateTimeOffset.UtcNow;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
}
