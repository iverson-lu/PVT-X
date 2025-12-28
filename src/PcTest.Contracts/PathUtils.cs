using System.Runtime.InteropServices;

namespace PcTest.Contracts;

public static class PathUtils
{
    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var separator = Path.DirectorySeparatorChar;
        if (path[^1] != separator)
        {
            return path + separator;
        }

        return path;
    }

    public static bool IsContainedBy(string root, string candidate)
    {
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var candidateFull = Path.GetFullPath(candidate);
        if (candidateFull.Length < rootFull.Length)
        {
            return false;
        }

        return string.Compare(candidateFull, 0, rootFull, 0, rootFull.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    public static string ResolvePathWithLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var remainder = fullPath[root.Length..];
        var segments = remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileSystemInfo? info = null;
            if (Directory.Exists(current))
            {
                info = new DirectoryInfo(current);
            }
            else if (File.Exists(current))
            {
                info = new FileInfo(current);
            }

            if (info != null && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = info.ResolveLinkTarget(true);
                if (target == null)
                {
                    return current;
                }

                current = Path.GetFullPath(target.FullName);
            }
        }

        return current;
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
