namespace PcTest.Contracts;

public static class PathUtilities
{
    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static bool IsContainedBy(string candidate, string root)
    {
        var normalizedRoot = EnsureTrailingSeparator(NormalizePath(root));
        var normalizedCandidate = NormalizePath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }
        return path + Path.DirectorySeparatorChar;
    }

    public static string ResolveReparsePoint(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            return path;
        }
        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return path;
        }
        var resolved = info.ResolveLinkTarget(true);
        return resolved?.FullName ?? path;
    }
}
