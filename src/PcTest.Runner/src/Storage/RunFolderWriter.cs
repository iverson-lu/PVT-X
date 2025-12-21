using System.Text.Json;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Serialization;

namespace PcTest.Runner.Storage;

/// <summary>
/// Creates and manages folders used to store run artifacts.
/// </summary>
public class RunFolderWriter
{
    private static readonly JsonSerializerOptions IndexJsonOptions = new(JsonDefaults.Options)
    {
        WriteIndented = false
    };

    /// <summary>
    /// Creates a new run folder, snapshots the manifest and parameters, and returns context details.
    /// </summary>
    /// <param name="manifest">Manifest describing the test being executed.</param>
    /// <param name="parameters">Bound parameters supplied for the run.</param>
    /// <param name="runsRoot">Root directory where run folders are stored.</param>
    /// <returns>Context describing the created run folder and artifact paths.</returns>
    public RunContext Create(TestManifest manifest, IReadOnlyDictionary<string, BoundParameterValue> parameters, string runsRoot)
    {
        Directory.CreateDirectory(runsRoot);

        var runId = GenerateRunId();
        var runFolder = Path.Combine(runsRoot, runId);
        Directory.CreateDirectory(runFolder);
        Directory.CreateDirectory(Path.Combine(runFolder, "artifacts"));

        var manifestSnapshotPath = Path.Combine(runFolder, "manifest.json");
        var paramsPath = Path.Combine(runFolder, "params.json");
        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        var envPath = Path.Combine(runFolder, "env.json");
        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        var stderrPath = Path.Combine(runFolder, "stderr.log");
        var resultPath = Path.Combine(runFolder, "result.json");

        File.WriteAllText(manifestSnapshotPath, JsonSerializer.Serialize(manifest, JsonDefaults.Options));

        var parameterSnapshot = parameters.ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(paramsPath, JsonSerializer.Serialize(parameterSnapshot, JsonDefaults.Options));

        File.Create(eventsPath).Dispose();
        File.Create(stdoutPath).Dispose();
        File.Create(stderrPath).Dispose();

        return new RunContext(runId, runFolder, manifestSnapshotPath, paramsPath, eventsPath, envPath, stdoutPath, stderrPath, resultPath);
    }

    /// <summary>
    /// Appends a run entry to the index log for quick lookup.
    /// </summary>
    /// <param name="runsRoot">Root directory containing all run folders.</param>
    /// <param name="runId">Identifier of the run.</param>
    /// <param name="testId">Identifier of the executed test.</param>
    /// <param name="start">Start time of the run.</param>
    /// <param name="end">End time of the run.</param>
    /// <param name="status">Result status of the run.</param>
    public void AppendIndex(string runsRoot, string runId, string testId, DateTimeOffset start, DateTimeOffset end, PcTest.Contracts.Result.TestStatus status)
    {
        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        Directory.CreateDirectory(runsRoot);

        var entry = new PcTest.Contracts.Result.RunIndexEntry(runId, testId, start, end, status);
        var line = JsonSerializer.Serialize(entry, IndexJsonOptions);
        File.AppendAllText(indexPath, line + Environment.NewLine);
    }

    private static string GenerateRunId()
    {
        return $"run-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }
}

/// <summary>
/// Represents the folder and artifact paths created for a single run.
/// </summary>
/// <param name="RunId">Unique identifier for the run.</param>
/// <param name="RunFolder">Root folder containing artifacts.</param>
/// <param name="ManifestPath">Path to the manifest snapshot stored for the run.</param>
/// <param name="ParamsPath">Path to the serialized parameter snapshot.</param>
/// <param name="EventsPath">Path to the event log file.</param>
/// <param name="EnvPath">Path to the environment snapshot.</param>
/// <param name="StdoutPath">Path capturing standard output from the script.</param>
/// <param name="StderrPath">Path capturing standard error from the script.</param>
/// <param name="ResultPath">Path to the serialized result payload.</param>
public record RunContext(
    string RunId,
    string RunFolder,
    string ManifestPath,
    string ParamsPath,
    string EventsPath,
    string EnvPath,
    string StdoutPath,
    string StderrPath,
    string ResultPath);
