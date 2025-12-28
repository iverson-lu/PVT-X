using System.Runtime.InteropServices;

namespace PcTest.Contracts;

public static class PathUtils
{
    public static bool IsContainedBy(string expectedRoot, string candidate)
    {
        string normalizedRoot = NormalizePath(expectedRoot);
        string normalizedCandidate = NormalizePath(candidate);
        StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return normalizedCandidate.StartsWith(normalizedRoot, comparison);
    }

    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static string ResolvePathWithLinks(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        string current = root;
        string[] segments = fullPath.Substring(root.Length).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            string next = Path.Combine(current, segment);
            if (Directory.Exists(next))
            {
                DirectoryInfo info = new(next);
                if (info.LinkTarget is not null)
                {
                    DirectoryInfo? target = info.ResolveLinkTarget(true);
                    if (target is null)
                    {
                        return fullPath;
                    }

                    current = Path.GetFullPath(target.FullName);
                    continue;
                }
            }
            else if (File.Exists(next))
            {
                FileInfo info = new(next);
                if (info.LinkTarget is not null)
                {
                    FileInfo? target = info.ResolveLinkTarget(true);
                    if (target is null)
                    {
                        return fullPath;
                    }

                    current = Path.GetDirectoryName(Path.GetFullPath(target.FullName)) ?? current;
                    continue;
                }
            }

            current = next;
        }

        return Path.GetFullPath(current);
    }

    public static string CombineNormalized(string root, string relative)
    {
        return Path.GetFullPath(Path.Combine(root, relative));
    }
}
