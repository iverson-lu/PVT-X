using System.IO;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for accessing run history.
/// </summary>
public sealed class RunRepository : IRunRepository
{
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;

    public RunRepository(ISettingsService settingsService, IFileSystemService fileSystemService)
    {
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
    }

    public async Task<IReadOnlyList<RunIndexEntry>> GetAllRunsAsync(CancellationToken cancellationToken = default)
    {
        return await GetRunsAsync(new RunFilter { TopLevelOnly = false, MaxResults = int.MaxValue }, cancellationToken);
    }

    public async Task<IReadOnlyList<RunIndexEntry>> GetRunsAsync(RunFilter filter, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        var indexPath = Path.Combine(settings.ResolvedRunsRoot, "index.jsonl");
        
        if (!_fileSystemService.FileExists(indexPath))
        {
            return Array.Empty<RunIndexEntry>();
        }
        
        var entries = new List<RunIndexEntry>();
        var content = await _fileSystemService.ReadAllTextAsync(indexPath, cancellationToken);
        
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var entry = ParseIndexEntry(line.Trim());
                if (entry is not null && MatchesFilter(entry, filter))
                {
                    entries.Add(entry);
                }
            }
            catch
            {
                // Skip malformed lines
            }
        }
        
        // Sort by start time descending and limit results
        return entries
            .OrderByDescending(e => e.StartTime)
            .Take(filter.MaxResults)
            .ToList();
    }

    private bool MatchesFilter(RunIndexEntry entry, RunFilter filter)
    {
        if (filter.TopLevelOnly && !string.IsNullOrEmpty(entry.ParentRunId))
        {
            return false;
        }
        
        if (filter.StartTimeFrom.HasValue && entry.StartTime < filter.StartTimeFrom.Value)
        {
            return false;
        }
        
        if (filter.StartTimeTo.HasValue && entry.StartTime > filter.StartTimeTo.Value)
        {
            return false;
        }
        
        if (filter.Status.HasValue && entry.Status != filter.Status.Value)
        {
            return false;
        }
        
        if (!string.IsNullOrEmpty(filter.TestId) && entry.TestId != filter.TestId)
        {
            return false;
        }
        
        if (!string.IsNullOrEmpty(filter.SuiteId) && entry.SuiteId != filter.SuiteId)
        {
            return false;
        }
        
        if (!string.IsNullOrEmpty(filter.PlanId) && entry.PlanId != filter.PlanId)
        {
            return false;
        }
        
        if (filter.RunType.HasValue && entry.RunType != filter.RunType.Value)
        {
            return false;
        }
        
        return true;
    }

    private RunIndexEntry? ParseIndexEntry(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        var entry = new RunIndexEntry
        {
            RunId = root.GetProperty("runId").GetString() ?? string.Empty
        };
        
        if (root.TryGetProperty("runType", out var runTypeProp))
        {
            entry.RunType = Enum.TryParse<RunType>(runTypeProp.GetString(), true, out var rt) ? rt : RunType.TestCase;
        }
        
        if (root.TryGetProperty("nodeId", out var nodeIdProp))
            entry.NodeId = nodeIdProp.GetString();
        if (root.TryGetProperty("testId", out var testIdProp))
            entry.TestId = testIdProp.GetString();
        if (root.TryGetProperty("testVersion", out var testVersionProp))
            entry.TestVersion = testVersionProp.GetString();
        if (root.TryGetProperty("suiteId", out var suiteIdProp))
            entry.SuiteId = suiteIdProp.GetString();
        if (root.TryGetProperty("suiteVersion", out var suiteVersionProp))
            entry.SuiteVersion = suiteVersionProp.GetString();
        if (root.TryGetProperty("planId", out var planIdProp))
            entry.PlanId = planIdProp.GetString();
        if (root.TryGetProperty("planVersion", out var planVersionProp))
            entry.PlanVersion = planVersionProp.GetString();
        if (root.TryGetProperty("parentRunId", out var parentRunIdProp))
            entry.ParentRunId = parentRunIdProp.GetString();
        
        if (root.TryGetProperty("startTime", out var startTimeProp) && DateTime.TryParse(startTimeProp.GetString(), out var startTime))
            entry.StartTime = startTime;
        if (root.TryGetProperty("endTime", out var endTimeProp) && DateTime.TryParse(endTimeProp.GetString(), out var endTime))
            entry.EndTime = endTime;
        
        if (root.TryGetProperty("status", out var statusProp))
            entry.Status = Enum.TryParse<RunStatus>(statusProp.GetString(), true, out var status) ? status : RunStatus.Failed;
        
        return entry;
    }

    public async Task<RunDetails?> GetRunDetailsAsync(string runId, CancellationToken cancellationToken = default)
    {
        var runFolder = GetRunFolderPath(runId);
        if (!_fileSystemService.DirectoryExists(runFolder))
        {
            return null;
        }
        
        var details = new RunDetails();
        
        // Get index entry
        var allRuns = await GetAllRunsAsync(cancellationToken);
        var indexEntry = allRuns.FirstOrDefault(r => r.RunId == runId);
        if (indexEntry is not null)
        {
            details.IndexEntry = indexEntry;
        }
        
        // Read result.json
        var resultPath = Path.Combine(runFolder, "result.json");
        if (_fileSystemService.FileExists(resultPath))
        {
            details.ResultJson = await _fileSystemService.ReadAllTextAsync(resultPath, cancellationToken);
        }
        
        // Read manifest.json
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        if (_fileSystemService.FileExists(manifestPath))
        {
            details.ManifestJson = await _fileSystemService.ReadAllTextAsync(manifestPath, cancellationToken);
        }
        
        // Read params.json
        var paramsPath = Path.Combine(runFolder, "params.json");
        if (_fileSystemService.FileExists(paramsPath))
        {
            details.ParamsJson = await _fileSystemService.ReadAllTextAsync(paramsPath, cancellationToken);
        }
        
        // Read env.json
        var envPath = Path.Combine(runFolder, "env.json");
        if (_fileSystemService.FileExists(envPath))
        {
            details.EnvJson = await _fileSystemService.ReadAllTextAsync(envPath, cancellationToken);
        }
        
        return details;
    }

    public async Task<IReadOnlyList<ArtifactInfo>> GetArtifactsAsync(string runId, CancellationToken cancellationToken = default)
    {
        var runFolder = GetRunFolderPath(runId);
        if (!_fileSystemService.DirectoryExists(runFolder))
        {
            return Array.Empty<ArtifactInfo>();
        }
        
        return await Task.Run(() => GetArtifactsRecursive(runFolder, runFolder), cancellationToken);
    }

    private List<ArtifactInfo> GetArtifactsRecursive(string rootFolder, string currentFolder)
    {
        var artifacts = new List<ArtifactInfo>();
        
        // Add files
        foreach (var filePath in _fileSystemService.GetFiles(currentFolder))
        {
            var fileInfo = _fileSystemService.GetFileInfo(filePath);
            artifacts.Add(new ArtifactInfo
            {
                Name = fileInfo.Name,
                RelativePath = Path.GetRelativePath(rootFolder, filePath),
                FullPath = filePath,
                Size = fileInfo.Length,
                IsDirectory = false
            });
        }
        
        // Add directories
        foreach (var dirPath in _fileSystemService.GetDirectories(currentFolder))
        {
            var dirName = Path.GetFileName(dirPath);
            var artifactDir = new ArtifactInfo
            {
                Name = dirName,
                RelativePath = Path.GetRelativePath(rootFolder, dirPath),
                FullPath = dirPath,
                IsDirectory = true,
                Children = GetArtifactsRecursive(rootFolder, dirPath)
            };
            artifacts.Add(artifactDir);
        }
        
        return artifacts;
    }

    public async Task<string> ReadArtifactAsync(string runId, string artifactPath, CancellationToken cancellationToken = default)
    {
        var runFolder = GetRunFolderPath(runId);
        var fullPath = Path.Combine(runFolder, artifactPath);
        
        if (!_fileSystemService.FileExists(fullPath))
        {
            throw new FileNotFoundException($"Artifact not found: {artifactPath}");
        }
        
        return await _fileSystemService.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async IAsyncEnumerable<EventBatch> StreamEventsAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runFolder = GetRunFolderPath(runId);
        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        
        if (!_fileSystemService.FileExists(eventsPath))
        {
            yield return new EventBatch { IsComplete = true };
            yield break;
        }
        
        var content = await _fileSystemService.ReadAllTextAsync(eventsPath, cancellationToken);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        const int batchSize = 50;
        var currentBatch = new List<StructuredEvent>();
        
        foreach (var line in lines)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            
            var evt = TryParseEvent(line.Trim());
            if (evt is not null)
            {
                currentBatch.Add(evt);
                
                if (currentBatch.Count >= batchSize)
                {
                    yield return new EventBatch { Events = new List<StructuredEvent>(currentBatch), IsComplete = false };
                    currentBatch.Clear();
                    await Task.Delay(10, cancellationToken); // Allow UI to update
                }
            }
        }
        
        // Final batch
        if (currentBatch.Count > 0)
        {
            yield return new EventBatch { Events = currentBatch, IsComplete = true };
        }
        else
        {
            yield return new EventBatch { IsComplete = true };
        }
    }

    private StructuredEvent? TryParseEvent(string json)
    {
        try
        {
            return ParseEvent(json);
        }
        catch
        {
            return null;
        }
    }

    private StructuredEvent? ParseEvent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        var evt = new StructuredEvent
        {
            RawJson = json
        };
        
        if (root.TryGetProperty("timestamp", out var timestampProp) && DateTime.TryParse(timestampProp.GetString(), out var ts))
            evt.Timestamp = ts;
        if (root.TryGetProperty("level", out var levelProp))
            evt.Level = levelProp.GetString() ?? "info";
        if (root.TryGetProperty("source", out var sourceProp))
            evt.Source = sourceProp.GetString();
        if (root.TryGetProperty("nodeId", out var nodeIdProp))
            evt.NodeId = nodeIdProp.GetString();
        if (root.TryGetProperty("type", out var typeProp))
            evt.Type = typeProp.GetString();
        if (root.TryGetProperty("code", out var codeProp))
            evt.Code = codeProp.GetString();
        if (root.TryGetProperty("message", out var messageProp))
            evt.Message = messageProp.GetString() ?? string.Empty;
        if (root.TryGetProperty("exception", out var exceptionProp))
            evt.Exception = exceptionProp.GetString();
        if (root.TryGetProperty("stack", out var stackProp))
            evt.StackTrace = stackProp.GetString();
        if (root.TryGetProperty("filePath", out var filePathProp))
            evt.FilePath = filePathProp.GetString();
        
        return evt;
    }

    public string GetRunFolderPath(string runId)
    {
        var settings = _settingsService.CurrentSettings;
        return Path.Combine(settings.ResolvedRunsRoot, runId);
    }

    public async Task<HistoryPurgePreview> PreviewHistoryPurgeAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var allRuns = await GetAllRunsAsync(cancellationToken);
        var candidates = allRuns
            .Where(entry => entry.StartTime != default && entry.StartTime < cutoff)
            .ToList();

        var preview = new HistoryPurgePreview
        {
            RunCount = candidates.Count
        };

        if (candidates.Count == 0)
        {
            return preview;
        }

        preview.EarliestRunTime = candidates.Min(entry => entry.StartTime);
        preview.LatestRunTime = candidates.Max(entry => entry.StartTime);
        preview.TotalArtifactSize = TryGetTotalArtifactSize(candidates);

        return preview;
    }

    public async Task<HistoryPurgeResult> PurgeHistoryAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");
        }

        var settings = _settingsService.CurrentSettings;
        var indexPath = Path.Combine(settings.ResolvedRunsRoot, "index.jsonl");

        if (!_fileSystemService.FileExists(indexPath))
        {
            return new HistoryPurgeResult();
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var lines = await _fileSystemService.ReadAllLinesAsync(indexPath, cancellationToken);
        var purgeEntries = new List<RunIndexEntry>();
        var purgeRunIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retainedLines = new List<string>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = ParseIndexEntry(line.Trim());
                if (entry is not null && entry.StartTime != default && entry.StartTime < cutoff)
                {
                    purgeEntries.Add(entry);
                    purgeRunIds.Add(entry.RunId);
                }
                else
                {
                    retainedLines.Add(line);
                }
            }
            catch
            {
                retainedLines.Add(line);
            }
        }

        if (purgeEntries.Count == 0)
        {
            return new HistoryPurgeResult();
        }

        var result = new HistoryPurgeResult
        {
            RunCount = purgeEntries.Count,
            EarliestRunTime = purgeEntries.Min(entry => entry.StartTime),
            LatestRunTime = purgeEntries.Max(entry => entry.StartTime),
            TotalArtifactSize = TryGetTotalArtifactSize(purgeEntries)
        };

        foreach (var runId in purgeRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runFolder = GetRunFolderPath(runId);
            if (_fileSystemService.DirectoryExists(runFolder))
            {
                _fileSystemService.DeleteDirectory(runFolder, true);
            }
        }

        var updatedContent = retainedLines.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, retainedLines) + Environment.NewLine;

        await _fileSystemService.WriteAllTextAsync(indexPath, updatedContent, cancellationToken);

        return result;
    }

    private long? TryGetTotalArtifactSize(IEnumerable<RunIndexEntry> entries)
    {
        try
        {
            long total = 0;
            foreach (var entry in entries)
            {
                var folderPath = GetRunFolderPath(entry.RunId);
                if (_fileSystemService.DirectoryExists(folderPath))
                {
                    total += GetDirectorySize(folderPath);
                }
            }

            return total;
        }
        catch
        {
            return null;
        }
    }

    private long GetDirectorySize(string folderPath)
    {
        long total = 0;
        foreach (var filePath in _fileSystemService.GetFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            total += _fileSystemService.GetFileInfo(filePath).Length;
        }

        return total;
    }
}
