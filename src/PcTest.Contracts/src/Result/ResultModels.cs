using System.Text.Json.Serialization;

namespace PcTest.Contracts.Result;

public class TestResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("testId")]
    public string TestId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestStatus Status { get; set; }

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset EndTime { get; set; }

    [JsonPropertyName("metrics")]
    public Dictionary<string, object>? Metrics { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("error")]
    public ResultError? Error { get; set; }

    [JsonPropertyName("runner")]
    public RunnerMetadata? Runner { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestStatus
{
    Passed,
    Failed,
    Error,
    Timeout
}

public class ResultError
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stack")]
    public string? Stack { get; set; }
}

public class RunnerMetadata
{
    [JsonPropertyName("runnerVersion")]
    public string RunnerVersion { get; set; } = string.Empty;

    [JsonPropertyName("powerShellVersion")]
    public string PowerShellVersion { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string? MachineName { get; set; }
}

public record RunIndexEntry(
    string RunId,
    string TestId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    TestStatus Status);

public class EnvironmentSnapshot
{
    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("runnerVersion")]
    public string? RunnerVersion { get; set; }

    [JsonPropertyName("powerShellVersion")]
    public string? PowerShellVersion { get; set; }

    [JsonPropertyName("isElevated")]
    public bool IsElevated { get; set; }
}
