using System.IO;
using PcTest.Contracts;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for accessing run history.
/// </summary>
public interface IRunRepository
{
    /// <summary>
    /// Gets all runs from the index.
    /// </summary>
    Task<IReadOnlyList<RunIndexEntry>> GetAllRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the run index for UI display.
    /// </summary>
    Task<IReadOnlyList<RunIndexEntryViewModel>> GetRunIndexAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets runs matching the specified filter.
    /// </summary>
    Task<IReadOnlyList<RunIndexEntry>> GetRunsAsync(RunFilter filter, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a run by ID.
    /// </summary>
    Task<RunDetails?> GetRunDetailsAsync(string runId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets artifacts for a run.
    /// </summary>
    Task<IReadOnlyList<ArtifactInfo>> GetArtifactsAsync(string runId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reads an artifact file.
    /// </summary>
    Task<string> ReadArtifactAsync(string runId, string artifactPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Streams events from events.jsonl with batched updates.
    /// </summary>
    IAsyncEnumerable<EventBatch> StreamEventsAsync(string runId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the run folder path.
    /// </summary>
    string GetRunFolderPath(string runId);
}

/// <summary>
/// Entry from the runs index.
/// </summary>
public sealed class RunIndexEntry
{
    public string RunId { get; set; } = string.Empty;
    public RunType RunType { get; set; }
    public string? NodeId { get; set; }
    public string? TestId { get; set; }
    public string? TestVersion { get; set; }
    public string? SuiteId { get; set; }
    public string? SuiteVersion { get; set; }
    public string? PlanId { get; set; }
    public string? PlanVersion { get; set; }
    public string? ParentRunId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public RunStatus Status { get; set; }
    
    public string DisplayName => RunType switch
    {
        RunType.TestCase => $"{TestId}@{TestVersion}",
        RunType.TestSuite => $"{SuiteId}@{SuiteVersion}",
        RunType.TestPlan => $"{PlanId}@{PlanVersion}",
        _ => RunId
    };
    
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
}

/// <summary>
/// Filter for querying runs.
/// </summary>
public sealed class RunFilter
{
    public DateTime? StartTimeFrom { get; set; }
    public DateTime? StartTimeTo { get; set; }
    public RunStatus? Status { get; set; }
    public string? TestId { get; set; }
    public string? SuiteId { get; set; }
    public string? PlanId { get; set; }
    public RunType? RunType { get; set; }
    public bool TopLevelOnly { get; set; } = true;
    public int MaxResults { get; set; } = 100;
}

/// <summary>
/// Detailed information about a run.
/// </summary>
public sealed class RunDetails
{
    public RunIndexEntry IndexEntry { get; set; } = new();
    public string? ResultJson { get; set; }
    public string? ManifestJson { get; set; }
    public string? ParamsJson { get; set; }
    public string? EnvJson { get; set; }
}

/// <summary>
/// Information about an artifact file.
/// </summary>
public sealed class ArtifactInfo
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public List<ArtifactInfo> Children { get; set; } = new();
}

/// <summary>
/// Batch of events for UI updates.
/// </summary>
public sealed class EventBatch
{
    public List<StructuredEvent> Events { get; set; } = new();
    public bool IsComplete { get; set; }
}

/// <summary>
/// Structured event from events.jsonl.
/// </summary>
public sealed class StructuredEvent
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "info";
    public string? Source { get; set; }
    public string? NodeId { get; set; }
    public string? Type { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? StackTrace { get; set; }
    public string? FilePath { get; set; }
    public string RawJson { get; set; } = string.Empty;
}
