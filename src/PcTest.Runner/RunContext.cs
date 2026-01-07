using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Results;

namespace PcTest.Runner;

/// <summary>
/// Context passed from Engine to Runner for executing a Test Case.
/// </summary>
public sealed class RunContext
{
    /// <summary>
    /// Unique run identifier.
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// The test case manifest.
    /// </summary>
    public TestCaseManifest Manifest { get; init; } = new();

    /// <summary>
    /// Resolved path to the test case folder containing run.ps1.
    /// </summary>
    public string TestCasePath { get; init; } = string.Empty;

    /// <summary>
    /// Effective inputs after all resolution (defaults, suite inputs, overrides).
    /// Values are already resolved (EnvRef -> literal).
    /// </summary>
    public Dictionary<string, object?> EffectiveInputs { get; init; } = new();

    /// <summary>
    /// Effective environment variables for the process.
    /// </summary>
    public Dictionary<string, string> EffectiveEnvironment { get; init; } = new();

    /// <summary>
    /// Metadata about which inputs are secret (for redaction).
    /// Key = parameter name, Value = true if secret.
    /// </summary>
    public Dictionary<string, bool> SecretInputs { get; init; } = new();

    /// <summary>
    /// Metadata about which environment variables are secret.
    /// </summary>
    public HashSet<string> SecretEnvVars { get; init; } = new();

    /// <summary>
    /// Working directory relative to Case Run Folder (optional).
    /// </summary>
    public string? WorkingDir { get; init; }

    /// <summary>
    /// Timeout in seconds. Null means no timeout.
    /// </summary>
    public int? TimeoutSec { get; init; }

    /// <summary>
    /// Phase for reboot-resume scenarios. Default is 0 for initial execution.
    /// </summary>
    public int Phase { get; init; }

    /// <summary>
    /// Existing run folder path for resume scenarios.
    /// </summary>
    public string? ExistingRunFolder { get; init; }

    /// <summary>
    /// Indicates this execution is resuming after a reboot.
    /// </summary>
    public bool IsResume { get; init; }

    /// <summary>
    /// Root folder for runs output.
    /// </summary>
    public string RunsRoot { get; init; } = string.Empty;

    /// <summary>
    /// Root folder for assets (typically the parent of TestCases folder).
    /// Used to locate shared PowerShell modules under assets/PowerShell/Modules.
    /// </summary>
    public string AssetsRoot { get; init; } = string.Empty;

    /// <summary>
    /// Node ID (for suite-triggered runs).
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// Suite identity (for suite-triggered runs).
    /// </summary>
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }

    /// <summary>
    /// Plan identity (for plan-triggered runs).
    /// </summary>
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }

    /// <summary>
    /// Parent run ID (for suite/plan-triggered runs).
    /// </summary>
    public string? ParentRunId { get; init; }

    /// <summary>
    /// Input templates before EnvRef resolution (for manifest snapshot).
    /// </summary>
    public Dictionary<string, JsonElement>? InputTemplates { get; init; }

    /// <summary>
    /// Whether this is a standalone run (not part of suite/plan).
    /// </summary>
    public bool IsStandalone => string.IsNullOrEmpty(NodeId) && string.IsNullOrEmpty(SuiteId);
}
