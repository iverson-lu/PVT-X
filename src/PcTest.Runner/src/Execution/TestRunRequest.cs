using PcTest.Contracts.Manifest;

namespace PcTest.Runner.Execution;

public class TestRunRequest
{
    public required TestManifest Manifest { get; init; }
    public required string ManifestPath { get; init; }
    public required string ScriptPath { get; init; }
    public required IReadOnlyDictionary<string, BoundParameterValue> Parameters { get; init; }
    public string RunsRoot { get; init; } = Path.Combine(Environment.CurrentDirectory, "Runs");
}

public record TestRunResponse(string RunFolder, PcTest.Contracts.Result.TestResult Result);
