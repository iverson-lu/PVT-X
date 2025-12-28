using System.Runtime.InteropServices;

namespace PcTest.Contracts;

public static class PathUtil
{
    public static string NormalizePath(string path) => Path.GetFullPath(path);

    public static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    public static bool IsContained(string rootPath, string candidatePath)
    {
        var root = EnsureTrailingSeparator(NormalizePath(rootPath));
        var candidate = NormalizePath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveLinkTargetPath(string path)
    {
        var info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path) as FileSystemInfo;
        if (info is null)
        {
            return NormalizePath(path);
        }

        if (info.LinkTarget is null)
        {
            return NormalizePath(path);
        }

        var resolved = info.ResolveLinkTarget(true);
        return resolved?.FullName is { Length: > 0 }
            ? NormalizePath(resolved.FullName)
            : NormalizePath(path);
    }

    public static string CombineAndNormalize(string root, string relative)
    {
        var combined = Path.Combine(root, relative);
        return NormalizePath(combined);
    }

    public static string GetPlatformSensitiveExe(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }

        return name;
    }
}
