using PcTest.Contracts.Manifest;

namespace PcTest.Runner.Execution;

/// <summary>
/// Represents the data required to execute a test script.
/// </summary>
public class TestRunRequest
{
    /// <summary>
    /// Manifest describing the test.
    /// </summary>
    public required TestManifest Manifest { get; init; }
    /// <summary>
    /// File path to the manifest used for this run.
    /// </summary>
    public required string ManifestPath { get; init; }
    /// <summary>
    /// File path to the PowerShell script to execute.
    /// </summary>
    public required string ScriptPath { get; init; }
    /// <summary>
    /// Bound parameter values supplied to the script.
    /// </summary>
    public required IReadOnlyDictionary<string, BoundParameterValue> Parameters { get; init; }
    /// <summary>
    /// Root folder where run artifacts should be written.
    /// </summary>
    public string RunsRoot { get; init; } = Path.Combine(Environment.CurrentDirectory, "Runs");
}

/// <summary>
/// Represents the outcome of a test run along with the artifacts location.
/// </summary>
/// <param name="RunFolder">Path to the folder containing run artifacts.</param>
/// <param name="Result">Structured test result for the run.</param>
public record TestRunResponse(string RunFolder, PcTest.Contracts.Result.TestResult Result);
