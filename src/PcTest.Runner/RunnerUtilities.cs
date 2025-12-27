using System.Diagnostics;
using PcTest.Contracts;

namespace PcTest.Runner;

internal static class RunnerUtilities
{
    public static void AppendArguments(ProcessStartInfo startInfo, IReadOnlyDictionary<string, object?> inputs)
    {
        foreach (var kvp in inputs)
        {
            if (kvp.Value is null)
            {
                continue;
            }
            startInfo.ArgumentList.Add($"-{kvp.Key}");
            AppendValue(startInfo, kvp.Value);
        }
    }

    public static (string status, string? errorType) MapExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => ("Passed", null),
            1 => ("Failed", null),
            _ => ("Error", "ScriptError")
        };
    }

    public static string? ValidateWorkingDir(string runFolder, string? workingDir)
    {
        var target = runFolder;
        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            target = Path.GetFullPath(Path.Combine(runFolder, workingDir));
        }
        var normalizedRun = PathUtilities.EnsureTrailingSeparator(PathUtilities.NormalizePath(runFolder));
        var normalizedTarget = PathUtilities.NormalizePath(target);
        var resolved = ResolveReparse(normalizedTarget);
        if (!resolved.StartsWith(normalizedRun, StringComparison.OrdinalIgnoreCase))
        {
            return "Working directory escapes run folder.";
        }
        return null;
    }

    public static Dictionary<string, object?> RedactInputs(Dictionary<string, object?> inputs, HashSet<string> secrets)
    {
        var redacted = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in inputs)
        {
            redacted[kvp.Key] = secrets.Contains(kvp.Key) ? "***" : kvp.Value;
        }
        return redacted;
    }

    public static string RedactText(string text, HashSet<string> secrets, Dictionary<string, object?> inputs)
    {
        var output = text;
        foreach (var name in secrets)
        {
            if (inputs.TryGetValue(name, out var value) && value is not null)
            {
                output = output.Replace(value.ToString(), "***", StringComparison.Ordinal);
            }
        }
        return output;
    }

    private static string ResolveReparse(string path)
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

    private static void AppendValue(ProcessStartInfo startInfo, object value)
    {
        switch (value)
        {
            case bool b:
                startInfo.ArgumentList.Add(b ? "$true" : "$false");
                break;
            case string s:
                startInfo.ArgumentList.Add(s);
                break;
            case int i:
                startInfo.ArgumentList.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double d:
                startInfo.ArgumentList.Add(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case string[] arr:
                foreach (var item in arr)
                {
                    startInfo.ArgumentList.Add(item);
                }
                break;
            case int[] arr:
                foreach (var item in arr)
                {
                    startInfo.ArgumentList.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                break;
            case double[] arr:
                foreach (var item in arr)
                {
                    startInfo.ArgumentList.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                break;
            case bool[] arr:
                foreach (var item in arr)
                {
                    startInfo.ArgumentList.Add(item ? "$true" : "$false");
                }
                break;
            default:
                startInfo.ArgumentList.Add(value.ToString() ?? string.Empty);
                break;
        }
    }
}
