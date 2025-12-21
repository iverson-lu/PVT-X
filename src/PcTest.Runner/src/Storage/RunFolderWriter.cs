using System.Text.Json;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Serialization;

namespace PcTest.Runner.Storage;

public class RunFolderWriter
{
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

    public void AppendIndex(string runsRoot, string runId, string testId, DateTimeOffset start, DateTimeOffset end, PcTest.Contracts.Result.TestStatus status)
    {
        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        Directory.CreateDirectory(runsRoot);

        var entry = new PcTest.Contracts.Result.RunIndexEntry(runId, testId, start, end, status);
        var line = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        File.AppendAllText(indexPath, line + Environment.NewLine);
    }

    private static string GenerateRunId()
    {
        return $"run-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }
}

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
