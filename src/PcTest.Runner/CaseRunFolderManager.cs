using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;

namespace PcTest.Runner;

/// <summary>
/// Manages Test Case Run Folder creation and artifacts.
/// Runner is the exclusive owner of Case Run Folders per spec section 12.1.
/// </summary>
public sealed class CaseRunFolderManager : IDisposable
{
    private readonly string _runsRoot;
    private readonly ConcurrentDictionary<string, StreamWriter> _stdoutWriters = new();
    private readonly ConcurrentDictionary<string, StreamWriter> _stderrWriters = new();
    private bool _disposed;

    public CaseRunFolderManager(string runsRoot)
    {
        _runsRoot = PathUtils.NormalizePath(runsRoot);
    }

    /// <summary>
    /// Disposes all open StreamWriters.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var writer in _stdoutWriters.Values)
        {
            try { writer.Dispose(); } catch { /* best effort */ }
        }
        _stdoutWriters.Clear();

        foreach (var writer in _stderrWriters.Values)
        {
            try { writer.Dispose(); } catch { /* best effort */ }
        }
        _stderrWriters.Clear();
    }

    /// <summary>
    /// Creates a new Case Run Folder with unique ID.
    /// Handles collisions defensively per spec section 12.
    /// </summary>
    public string CreateRunFolder(string runId)
    {
        var folderPath = Path.Combine(_runsRoot, runId);
        var normalizedPath = PathUtils.NormalizePath(folderPath);

        // Handle collision by appending suffix
        var attempts = 0;
        var finalPath = normalizedPath;
        while (Directory.Exists(finalPath))
        {
            attempts++;
            finalPath = PathUtils.NormalizePath(Path.Combine(_runsRoot, $"{runId}_{attempts}"));
        }

        Directory.CreateDirectory(finalPath);

        // Create artifacts subdirectory per spec section 12.1
        Directory.CreateDirectory(Path.Combine(finalPath, "artifacts"));

        return finalPath;
    }

    /// <summary>
    /// Validates and creates working directory.
    /// Per spec section 6.5: workingDir must resolve inside Case Run Folder.
    /// </summary>
    public (bool Success, string? ResolvedPath, string? Error) PrepareWorkingDir(
        string caseRunFolder,
        string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return (true, caseRunFolder, null);
        }

        // Resolve relative path against Case Run Folder
        var resolvedPath = Path.IsPathRooted(workingDir)
            ? PathUtils.NormalizePath(workingDir)
            : PathUtils.Combine(caseRunFolder, workingDir);

        // Check containment per spec section 6.5
        var (isContained, finalPath, error) = PathUtils.CheckContainmentWithSymlinkResolution(
            resolvedPath, caseRunFolder);

        if (!isContained)
        {
            return (false, null,
                $"workingDir '{workingDir}' resolves outside Case Run Folder. " +
                $"Resolved: {finalPath ?? resolvedPath}, Expected root: {caseRunFolder}, Reason: OutOfRoot");
        }

        // Create directory if needed
        var targetPath = finalPath ?? resolvedPath;
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        return (true, targetPath, null);
    }

    /// <summary>
    /// Writes manifest.json snapshot to Case Run Folder.
    /// Applies redaction for secret values per spec section 12.2.
    /// </summary>
    public async Task WriteManifestSnapshotAsync(
        string caseRunFolder,
        CaseManifestSnapshot snapshot,
        Dictionary<string, bool>? secretInputs)
    {
        var redactedSnapshot = RedactSnapshot(snapshot, secretInputs);
        var json = JsonDefaults.Serialize(redactedSnapshot);
        var path = Path.Combine(caseRunFolder, "manifest.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes params.json to Case Run Folder.
    /// Applies redaction for secret values.
    /// </summary>
    public async Task WriteParamsAsync(
        string caseRunFolder,
        Dictionary<string, object?> effectiveInputs,
        Dictionary<string, bool>? secretInputs)
    {
        var redactedInputs = RedactInputs(effectiveInputs, secretInputs);
        var json = JsonDefaults.Serialize(redactedInputs);
        var path = Path.Combine(caseRunFolder, "params.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes stdout.log.
    /// Redacts secret values.
    /// </summary>
    public async Task WriteStdoutAsync(
        string caseRunFolder,
        string content,
        IEnumerable<string>? secretValues)
    {
        var redacted = RedactContent(content, secretValues);
        var path = Path.Combine(caseRunFolder, "stdout.log");
        await File.WriteAllTextAsync(path, redacted, Encoding.UTF8);
    }

    /// <summary>
    /// Writes stderr.log.
    /// Redacts secret values.
    /// </summary>
    public async Task WriteStderrAsync(
        string caseRunFolder,
        string content,
        IEnumerable<string>? secretValues)
    {
        var redacted = RedactContent(content, secretValues);
        var path = Path.Combine(caseRunFolder, "stderr.log");
        await File.WriteAllTextAsync(path, redacted, Encoding.UTF8);
    }

    /// <summary>
    /// Gets or creates a StreamWriter for stdout.log with FileShare.ReadWrite for real-time tailing.
    /// </summary>
    private StreamWriter GetOrCreateStdoutWriter(string caseRunFolder)
    {
        return _stdoutWriters.GetOrAdd(caseRunFolder, folder =>
        {
            var path = Path.Combine(folder, "stdout.log");
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        });
    }

    /// <summary>
    /// Gets or creates a StreamWriter for stderr.log with FileShare.ReadWrite for real-time tailing.
    /// </summary>
    private StreamWriter GetOrCreateStderrWriter(string caseRunFolder)
    {
        return _stderrWriters.GetOrAdd(caseRunFolder, folder =>
        {
            var path = Path.Combine(folder, "stderr.log");
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        });
    }

    /// <summary>
    /// Appends a line to stdout.log in real-time with redaction.
    /// Used during execution for streaming output.
    /// </summary>
    public async Task AppendStdoutLineAsync(
        string caseRunFolder,
        string? line,
        IEnumerable<string>? secretValues)
    {
        if (line is null) return;
        var redacted = RedactContent(line, secretValues);
        var writer = GetOrCreateStdoutWriter(caseRunFolder);
        await writer.WriteLineAsync(redacted);
    }

    /// <summary>
    /// Appends a line to stderr.log in real-time with redaction.
    /// Used during execution for streaming output.
    /// </summary>
    public async Task AppendStderrLineAsync(
        string caseRunFolder,
        string? line,
        IEnumerable<string>? secretValues)
    {
        if (line is null) return;
        var redacted = RedactContent(line, secretValues);
        var writer = GetOrCreateStderrWriter(caseRunFolder);
        await writer.WriteLineAsync(redacted);
    }

    /// <summary>
    /// Flushes and closes the stdout/stderr writers for a specific run folder.
    /// Must be called when execution completes.
    /// </summary>
    public void FlushAndCloseWriters(string caseRunFolder)
    {
        if (_stdoutWriters.TryRemove(caseRunFolder, out var stdoutWriter))
        {
            try
            {
                stdoutWriter.Flush();
                stdoutWriter.Dispose();
            }
            catch { /* best effort */ }
        }

        if (_stderrWriters.TryRemove(caseRunFolder, out var stderrWriter))
        {
            try
            {
                stderrWriter.Flush();
                stderrWriter.Dispose();
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Writes env.json (execution environment snapshot) per spec section 12.4.
    /// </summary>
    public async Task WriteEnvSnapshotAsync(
        string caseRunFolder,
        EnvironmentSnapshot snapshot)
    {
        var json = JsonDefaults.Serialize(snapshot);
        var path = Path.Combine(caseRunFolder, "env.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes result.json to Case Run Folder.
    /// Runner is the sole authority per spec section 13.1.
    /// Applies redaction for secret values.
    /// </summary>
    public async Task WriteResultAsync(
        string caseRunFolder,
        TestCaseResult result,
        Dictionary<string, bool>? secretInputs)
    {
        var redactedResult = RedactResult(result, secretInputs);
        var json = JsonDefaults.Serialize(redactedResult);
        var path = Path.Combine(caseRunFolder, "result.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Appends an event to events.jsonl.
    /// Uses retry logic to handle concurrent write scenarios.
    /// </summary>
    public async Task AppendEventAsync(string caseRunFolder, EventEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonDefaults.WriteOptions)
            .Replace("\r\n", "").Replace("\n", "") + Environment.NewLine;
        var path = Path.Combine(caseRunFolder, "events.jsonl");
        
        // Retry up to 3 times with exponential backoff for file locking issues
        var maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await File.AppendAllTextAsync(path, line, Encoding.UTF8);
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File is locked, wait and retry
                await Task.Delay(10 * (attempt + 1)); // 10ms, 20ms, 30ms
            }
        }
    }

    private static CaseManifestSnapshot RedactSnapshot(
        CaseManifestSnapshot snapshot,
        Dictionary<string, bool>? secretInputs)
    {
        if (secretInputs is null || secretInputs.Count == 0)
            return snapshot;

        return new CaseManifestSnapshot
        {
            SourceManifest = snapshot.SourceManifest,
            ResolvedRef = snapshot.ResolvedRef,
            ResolvedIdentity = snapshot.ResolvedIdentity,
            EffectiveEnvironment = snapshot.EffectiveEnvironment,
            EffectiveInputs = RedactInputs(snapshot.EffectiveInputs, secretInputs),
            InputTemplates = snapshot.InputTemplates,
            ResolvedAt = snapshot.ResolvedAt,
            EngineVersion = snapshot.EngineVersion
        };
    }

    private static Dictionary<string, object?> RedactInputs(
        Dictionary<string, object?> inputs,
        Dictionary<string, bool>? secretInputs)
    {
        if (secretInputs is null || secretInputs.Count == 0)
            return inputs;

        var redacted = new Dictionary<string, object?>(inputs.Count);
        foreach (var (key, value) in inputs)
        {
            if (secretInputs.TryGetValue(key, out var isSecret) && isSecret)
            {
                redacted[key] = "***";
            }
            else
            {
                redacted[key] = value;
            }
        }
        return redacted;
    }

    private static TestCaseResult RedactResult(
        TestCaseResult result,
        Dictionary<string, bool>? secretInputs)
    {
        if (secretInputs is null || secretInputs.Count == 0)
            return result;

        return new TestCaseResult
        {
            SchemaVersion = result.SchemaVersion,
            RunType = result.RunType,
            NodeId = result.NodeId,
            TestId = result.TestId,
            TestVersion = result.TestVersion,
            SuiteId = result.SuiteId,
            SuiteVersion = result.SuiteVersion,
            PlanId = result.PlanId,
            PlanVersion = result.PlanVersion,
            Status = result.Status,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Metrics = result.Metrics,
            Message = result.Message,
            ExitCode = result.ExitCode,
            EffectiveInputs = RedactInputs(result.EffectiveInputs, secretInputs),
            Error = result.Error,
            Runner = result.Runner
        };
    }

    private static string RedactContent(string content, IEnumerable<string>? secretValues)
    {
        if (secretValues is null)
            return content;

        var result = content;
        foreach (var secret in secretValues)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                result = result.Replace(secret, "***");
            }
        }
        return result;
    }
}
