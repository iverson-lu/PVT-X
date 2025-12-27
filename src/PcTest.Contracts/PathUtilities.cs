using System.Runtime.InteropServices;

namespace PcTest.Contracts;

public static class PathUtilities
{
    public static StringComparison PathComparison => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static bool IsPathContained(string root, string candidate)
    {
        var normalizedRoot = EnsureTrailingSeparator(NormalizePath(root));
        var normalizedCandidate = NormalizePath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, PathComparison);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    public static string ResolveLinkTarget(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            return NormalizePath(path);
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            var target = info.ResolveLinkTarget(true);
            if (target is null)
            {
                throw new IOException($"Unable to resolve link target for {path}");
            }

            return NormalizePath(target.FullName);
        }

        return NormalizePath(info.FullName);
    }

    public static string ResolvePathWithReparsePoints(string root, string relative)
    {
        var combined = NormalizePath(Path.Combine(root, relative));
        if (!combined.StartsWith(EnsureTrailingSeparator(NormalizePath(root)), PathComparison))
        {
            return combined;
        }

        var segments = combined.Substring(EnsureTrailingSeparator(NormalizePath(root)).Length)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = NormalizePath(root);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var target = info.ResolveLinkTarget(true);
                    if (target is null)
                    {
                        throw new IOException($"Unable to resolve link target for {current}");
                    }

                    current = NormalizePath(target.FullName);
                }
                else
                {
                    current = NormalizePath(current);
                }
            }
            else
            {
                current = NormalizePath(current);
            }
        }

        return NormalizePath(current);
    }
}
