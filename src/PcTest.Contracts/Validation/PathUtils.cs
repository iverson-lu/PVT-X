namespace PcTest.Contracts.Validation;

/// <summary>
/// Path utilities for Windows case-insensitive comparison and containment checks.
/// Per spec section 5.2 and 6.5.
/// </summary>
public static class PathUtils
{
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Normalizes a path to canonical absolute form.
    /// Resolves ., .., and symlinks/junctions.
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        // Get full path (resolves relative paths, ., ..)
        var fullPath = Path.GetFullPath(path);

        // Normalize directory separators to backslash on Windows
        fullPath = fullPath.Replace('/', '\\');

        // Remove trailing separator unless it's a root
        if (fullPath.Length > 1 && fullPath.EndsWith('\\') && !fullPath.EndsWith(":\\"))
        {
            fullPath = fullPath.TrimEnd('\\');
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves symlinks, junctions, and reparse points to get the final target path.
    /// Returns null if resolution fails.
    /// </summary>
    public static string? ResolveFinalTarget(string path)
    {
        try
        {
            var normalized = NormalizePath(path);

            // Check if path exists
            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                return null;
            }

            // Use FileInfo/DirectoryInfo to resolve symlinks
            // On Windows, ResolveLinkTarget recursively resolves the target
            var fileInfo = new FileInfo(normalized);
            if (fileInfo.Exists)
            {
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName ?? normalized;
            }

            var dirInfo = new DirectoryInfo(normalized);
            if (dirInfo.Exists)
            {
                var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName ?? normalized;
            }

            return normalized;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if childPath is contained within parentPath.
    /// Uses case-insensitive comparison for Windows.
    /// Both paths should be normalized first.
    /// </summary>
    public static bool IsContainedIn(string childPath, string parentPath)
    {
        var normalizedChild = NormalizePath(childPath);
        var normalizedParent = NormalizePath(parentPath);

        // Ensure parent ends with separator for proper prefix matching
        if (!normalizedParent.EndsWith('\\'))
        {
            normalizedParent += '\\';
        }

        // Child must either equal parent (without trailing separator) or start with parent + separator
        var parentWithoutTrailing = normalizedParent.TrimEnd('\\');
        
        return string.Equals(normalizedChild, parentWithoutTrailing, PathComparison) ||
               normalizedChild.StartsWith(normalizedParent, PathComparison);
    }

    /// <summary>
    /// Checks containment after resolving symlinks/junctions.
    /// Returns (isContained, resolvedPath, error).
    /// </summary>
    public static (bool IsContained, string? ResolvedPath, string? Error) CheckContainmentWithSymlinkResolution(
        string childPath,
        string parentPath)
    {
        try
        {
            var normalizedParent = NormalizePath(parentPath);

            // Resolve the child path (may be a symlink/junction)
            var resolvedChild = ResolveFinalTarget(childPath);
            if (resolvedChild is null)
            {
                // Path doesn't exist, just normalize it
                resolvedChild = NormalizePath(childPath);
            }

            // Also resolve parent in case it's a symlink
            var resolvedParent = ResolveFinalTarget(parentPath);
            if (resolvedParent is null)
            {
                resolvedParent = normalizedParent;
            }

            var isContained = IsContainedIn(resolvedChild, resolvedParent);
            return (isContained, resolvedChild, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Combines base path with relative path and normalizes the result.
    /// </summary>
    public static string Combine(string basePath, string relativePath)
    {
        return NormalizePath(Path.Combine(basePath, relativePath));
    }

    /// <summary>
    /// Checks if two paths are equal (case-insensitive on Windows).
    /// </summary>
    public static bool PathEquals(string path1, string path2)
    {
        return string.Equals(NormalizePath(path1), NormalizePath(path2), PathComparison);
    }

    /// <summary>
    /// Makes a path relative to a base path.
    /// </summary>
    public static string MakeRelative(string fullPath, string basePath)
    {
        var normalizedFull = NormalizePath(fullPath);
        var normalizedBase = NormalizePath(basePath);

        if (!normalizedBase.EndsWith('\\'))
            normalizedBase += '\\';

        if (normalizedFull.StartsWith(normalizedBase, PathComparison))
        {
            return normalizedFull.Substring(normalizedBase.Length);
        }

        return normalizedFull;
    }

    /// <summary>
    /// Ensures a directory exists, creating it if necessary.
    /// </summary>
    public static void EnsureDirectoryExists(string path)
    {
        var normalized = NormalizePath(path);
        if (!Directory.Exists(normalized))
        {
            Directory.CreateDirectory(normalized);
        }
    }

    /// <summary>
    /// Generates a safe folder name for Windows.
    /// Removes invalid characters and reserved names.
    /// </summary>
    public static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());

        // Reserved Windows names
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (reserved.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        // Ensure non-empty
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "_";
        }

        return sanitized;
    }
}
