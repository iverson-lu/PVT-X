using System.Text;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;

namespace PcTest.Engine.Execution;

/// <summary>
/// Manages Group Run Folders (Suite/Plan) and index.jsonl.
/// Engine is the exclusive owner per spec section 12.1.
/// </summary>
public sealed class GroupRunFolderManager
{
    private readonly string _runsRoot;
    private readonly object _indexLock = new();

    public GroupRunFolderManager(string runsRoot)
    {
        _runsRoot = PathUtils.NormalizePath(runsRoot);
        PathUtils.EnsureDirectoryExists(_runsRoot);
    }

    /// <summary>
    /// Creates a new Group Run Folder with unique ID.
    /// </summary>
    public string CreateGroupRunFolder(string groupRunId)
    {
        var folderPath = Path.Combine(_runsRoot, groupRunId);
        var normalizedPath = PathUtils.NormalizePath(folderPath);

        // Handle collision
        var attempts = 0;
        var finalPath = normalizedPath;
        while (Directory.Exists(finalPath))
        {
            attempts++;
            finalPath = PathUtils.NormalizePath(Path.Combine(_runsRoot, $"{groupRunId}_{attempts}"));
        }

        Directory.CreateDirectory(finalPath);
        return finalPath;
    }

    /// <summary>
    /// Returns an existing Group Run Folder for a given runId or absolute path.
    /// </summary>
    public string GetExistingGroupRunFolder(string runIdOrPath)
    {
        var resolvedPath = Path.IsPathRooted(runIdOrPath)
            ? PathUtils.NormalizePath(runIdOrPath)
            : PathUtils.NormalizePath(Path.Combine(_runsRoot, runIdOrPath));

        if (!Directory.Exists(resolvedPath))
        {
            throw new DirectoryNotFoundException($"Run folder not found: {resolvedPath}");
        }

        return resolvedPath;
    }

    /// <summary>
    /// Writes manifest.json for Suite/Plan run.
    /// </summary>
    public async Task WriteManifestAsync(string groupRunFolder, GroupManifestSnapshot snapshot)
    {
        var json = JsonDefaults.Serialize(snapshot);
        var path = Path.Combine(groupRunFolder, "manifest.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes controls.json for Suite run.
    /// </summary>
    public async Task WriteControlsAsync(string groupRunFolder, SuiteControls controls)
    {
        var json = JsonDefaults.Serialize(controls);
        var path = Path.Combine(groupRunFolder, "controls.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes environment.json for Suite/Plan run.
    /// </summary>
    public async Task WriteEnvironmentAsync(string groupRunFolder, Dictionary<string, string> effectiveEnv)
    {
        var json = JsonDefaults.Serialize(effectiveEnv);
        var path = Path.Combine(groupRunFolder, "environment.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes runRequest.json.
    /// </summary>
    public async Task WriteRunRequestAsync(string groupRunFolder, object runRequest)
    {
        var json = JsonDefaults.Serialize(runRequest);
        var path = Path.Combine(groupRunFolder, "runRequest.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes result.json for Suite/Plan run.
    /// </summary>
    public async Task WriteResultAsync(string groupRunFolder, GroupResult result)
    {
        var json = JsonDefaults.Serialize(result);
        var path = Path.Combine(groupRunFolder, "result.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// Appends to children.jsonl.
    /// Uses retry logic to handle concurrent write scenarios.
    /// </summary>
    public async Task AppendChildAsync(string groupRunFolder, ChildEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonDefaults.WriteOptions)
            .Replace("\r\n", "").Replace("\n", "") + Environment.NewLine;
        var path = Path.Combine(groupRunFolder, "children.jsonl");
        
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

    /// <summary>
    /// Appends an event to events.jsonl.
    /// Uses retry logic to handle concurrent write scenarios.
    /// </summary>
    public async Task AppendEventAsync(string groupRunFolder, EventEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonDefaults.WriteOptions)
            .Replace("\r\n", "").Replace("\n", "") + Environment.NewLine;
        var path = Path.Combine(groupRunFolder, "events.jsonl");
        
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

    /// <summary>
    /// Appends an entry to index.jsonl.
    /// Engine is the single writer per spec section 12.3.
    /// Uses lock for thread safety.
    /// </summary>
    public void AppendIndexEntry(IndexEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonDefaults.WriteOptions)
            .Replace("\r\n", "").Replace("\n", "") + Environment.NewLine;
        var path = Path.Combine(_runsRoot, "index.jsonl");

        lock (_indexLock)
        {
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Generates a unique group run ID.
    /// </summary>
    public static string GenerateGroupRunId(string prefix)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{timestamp}-{shortGuid}";
    }
}
