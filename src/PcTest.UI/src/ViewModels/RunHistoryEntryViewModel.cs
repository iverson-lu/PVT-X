using PcTest.Contracts.Result;

namespace PcTest.UI.ViewModels;

/// <summary>
/// View model representing a run history entry.
/// </summary>
public class RunHistoryEntryViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunHistoryEntryViewModel"/> class.
    /// </summary>
    public RunHistoryEntryViewModel(RunIndexEntry entry, string runFolder)
    {
        RunId = entry.RunId;
        TestId = entry.TestId;
        StartTime = entry.StartTime;
        EndTime = entry.EndTime;
        Status = entry.Status;
        RunFolder = runFolder;
    }

    /// <summary>
    /// Run identifier.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// Identifier of the executed test.
    /// </summary>
    public string TestId { get; }

    /// <summary>
    /// Start time of the run.
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// End time of the run.
    /// </summary>
    public DateTimeOffset EndTime { get; }

    /// <summary>
    /// Final status reported by the runner.
    /// </summary>
    public TestStatus Status { get; }

    /// <summary>
    /// Path to the run folder.
    /// </summary>
    public string RunFolder { get; }
}
