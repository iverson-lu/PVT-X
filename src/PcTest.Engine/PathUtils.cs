using System.Runtime.InteropServices;

namespace PcTest.Engine;

public static class PathUtils
{
    public static string GetCanonicalPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }

    public static bool IsContained(string rootPath, string candidatePath)
    {
        string root = EnsureTrailingSeparator(GetCanonicalPath(rootPath));
        string candidate = EnsureTrailingSeparator(GetCanonicalPath(candidatePath));
        StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.StartsWith(root, comparison);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    public static string ResolveLinkTargetIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        DirectoryInfo info = new(path);
        DirectoryInfo? resolved = info.ResolveLinkTarget(true);
        return resolved?.FullName ?? info.FullName;
    }
}
