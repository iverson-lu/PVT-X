using System.Text.Json.Serialization;

namespace PcTest.Contracts.Result;

/// <summary>
/// Represents the outcome of a single test execution.
/// </summary>
public class TestResult
{
    /// <summary>
    /// Schema version for the result payload.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Identifier of the test that produced this result.
    /// </summary>
    [JsonPropertyName("testId")]
    public string TestId { get; set; } = string.Empty;

    /// <summary>
    /// Final status of the test run.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestStatus Status { get; set; }

    /// <summary>
    /// UTC timestamp when execution started.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// UTC timestamp when execution completed.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTimeOffset EndTime { get; set; }

    /// <summary>
    /// Optional metrics emitted by the test.
    /// </summary>
    [JsonPropertyName("metrics")]
    public Dictionary<string, object>? Metrics { get; set; }

    /// <summary>
    /// Optional human readable message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Exit code returned by the PowerShell script.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    /// <summary>
    /// Optional error details when the test failed.
    /// </summary>
    [JsonPropertyName("error")]
    public ResultError? Error { get; set; }

    /// <summary>
    /// Metadata about the runner environment.
    /// </summary>
    [JsonPropertyName("runner")]
    public RunnerMetadata? Runner { get; set; }
}

/// <summary>
/// Represents the status of a test run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestStatus
{
    /// <summary>
    /// Test finished successfully.
    /// </summary>
    Passed,
    /// <summary>
    /// Test executed but reported a failure.
    /// </summary>
    Failed,
    /// <summary>
    /// Test encountered an unexpected error.
    /// </summary>
    Error,
    /// <summary>
    /// Test exceeded its allotted time.
    /// </summary>
    Timeout
}

/// <summary>
/// Describes an error encountered during a test run.
/// </summary>
public class ResultError
{
    /// <summary>
    /// Category of the error (for example Timeout or ScriptError).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Origin of the error such as Runner or Script.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Message describing the error condition.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Optional stack trace associated with the error.
    /// </summary>
    [JsonPropertyName("stack")]
    public string? Stack { get; set; }
}

/// <summary>
/// Metadata describing the environment that executed the test.
/// </summary>
public class RunnerMetadata
{
    /// <summary>
    /// Version of the PcTest runner.
    /// </summary>
    [JsonPropertyName("runnerVersion")]
    public string RunnerVersion { get; set; } = string.Empty;

    /// <summary>
    /// Version of PowerShell used during execution.
    /// </summary>
    [JsonPropertyName("powerShellVersion")]
    public string PowerShellVersion { get; set; } = string.Empty;

    /// <summary>
    /// Machine name where the test ran.
    /// </summary>
    [JsonPropertyName("machineName")]
    public string? MachineName { get; set; }
}

/// <summary>
/// A summarized entry for the run index log.
/// </summary>
/// <param name="RunId">Unique identifier of the run.</param>
/// <param name="TestId">Identifier of the test executed.</param>
/// <param name="StartTime">Start time of the run.</param>
/// <param name="EndTime">End time of the run.</param>
/// <param name="Status">Final status of the run.</param>
public record RunIndexEntry(
    string RunId,
    string TestId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    TestStatus Status);

/// <summary>
/// Captures environmental details at the time of test execution.
/// </summary>
public class EnvironmentSnapshot
{
    /// <summary>
    /// Operating system version string.
    /// </summary>
    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    /// <summary>
    /// Version of the PcTest runner.
    /// </summary>
    [JsonPropertyName("runnerVersion")]
    public string? RunnerVersion { get; set; }

    /// <summary>
    /// Version of PowerShell in use.
    /// </summary>
    [JsonPropertyName("powerShellVersion")]
    public string? PowerShellVersion { get; set; }

    /// <summary>
    /// Indicates whether the process was elevated.
    /// </summary>
    [JsonPropertyName("isElevated")]
    public bool IsElevated { get; set; }
}
