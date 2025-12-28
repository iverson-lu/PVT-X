using System.Runtime.InteropServices;

namespace PcTest.Contracts;

public static class PathUtils
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static string NormalizeAbsolute(string path)
    {
        return Path.GetFullPath(path);
    }

    public static bool IsContained(string root, string path)
    {
        var normalizedRoot = TrimEndingSeparator(NormalizeAbsolute(root));
        var normalizedPath = TrimEndingSeparator(NormalizeAbsolute(path));
        if (normalizedRoot.Length == 0)
        {
            return false;
        }

        var rootSegments = SplitSegments(normalizedRoot);
        var pathSegments = SplitSegments(normalizedPath);
        if (pathSegments.Length < rootSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < rootSegments.Length; i++)
        {
            if (!PathComparer.Equals(rootSegments[i], pathSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static string TrimEndingSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string ResolveLinkTargetPath(string path)
    {
        var full = NormalizeAbsolute(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        var segments = SplitSegments(full[root.Length..]);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (!info.Exists)
            {
                continue;
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = info.ResolveLinkTarget(true);
                if (target is not null)
                {
                    current = target.FullName;
                }
            }
        }

        if (File.Exists(full))
        {
            var fileInfo = new FileInfo(full);
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = fileInfo.ResolveLinkTarget(true);
                if (target is not null)
                {
                    return target.FullName;
                }
            }
        }

        return current;
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string[] SplitSegments(string path)
    {
        return path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
    }
}
