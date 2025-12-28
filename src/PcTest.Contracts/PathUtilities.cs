namespace PcTest.Contracts;

public static class PathUtilities
{
    public static string GetCanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? resolved = ResolveLinkTarget(fullPath);
        return resolved ?? fullPath;
    }

    public static bool IsContained(string root, string target)
    {
        string canonicalRoot = GetCanonicalPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string canonicalTarget = GetCanonicalPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(canonicalRoot, canonicalTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!canonicalRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalRoot += Path.DirectorySeparatorChar;
        }

        return canonicalTarget.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveLinkTarget(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            FileSystemInfo? target = fileInfo.ResolveLinkTarget(true);
            return target?.FullName;
        }

        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            FileSystemInfo? target = dirInfo.ResolveLinkTarget(true);
            return target?.FullName;
        }

        return null;
    }
}
