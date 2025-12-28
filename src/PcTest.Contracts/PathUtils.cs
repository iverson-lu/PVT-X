namespace PcTest.Contracts;

public static class PathUtils
{
    public static string NormalizePath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        return Path.GetFullPath(path);
    }

    public static bool IsContained(string root, string candidate)
    {
        var rootPath = NormalizePath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidatePath = NormalizePath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(rootPath, candidatePath, comparison))
        {
            return true;
        }

        return candidatePath.StartsWith(rootPath + Path.DirectorySeparatorChar, comparison);
    }

    public static string ResolveFinalPath(string path)
    {
        var fullPath = NormalizePath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return fullPath;
        }

        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return fullPath;
        }

        var target = info.ResolveLinkTarget(true);
        if (target is null)
        {
            return fullPath;
        }

        return NormalizePath(target.FullName);
    }

    public static string ResolveFinalDirectory(string path)
    {
        var fullPath = NormalizePath(path);
        var info = new DirectoryInfo(fullPath);
        if (!info.Exists)
        {
            return fullPath;
        }

        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return fullPath;
        }

        var target = info.ResolveLinkTarget(true);
        if (target is null)
        {
            return fullPath;
        }

        return NormalizePath(target.FullName);
    }
}
