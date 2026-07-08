using System.Text.RegularExpressions;

namespace DiskWriteWatch.Core;

public static partial class PathHelpers
{
    public static (string Directory, string Extension) Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ("<unknown>", string.Empty);
        try
        {
            return (Path.GetDirectoryName(path) ?? "<root>", Path.GetExtension(path));
        }
        catch (ArgumentException)
        {
            return ("<invalid>", string.Empty);
        }
    }

    public static string GetProcessRole(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return "process";
        var match = ProcessTypeRegex().Match(commandLine);
        if (match.Success)
            return match.Groups[1].Value;
        return commandLine.Contains("--headless", StringComparison.OrdinalIgnoreCase) ? "headless" : "browser";
    }

    [GeneratedRegex("""(?:^|\s)--type=([^\s"]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProcessTypeRegex();
}
