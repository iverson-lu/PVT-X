using System.Runtime.InteropServices;
using System.Security.Principal;
using PcTest.Contracts;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;

namespace PcTest.Runner;

/// <summary>
/// Test Case Runner - the authority for Test Case execution per spec section 4.3 and 4.4.
/// </summary>
public sealed class TestCaseRunner
{
    public const string RunnerVersion = "1.0.0";

    private readonly CancellationToken _cancellationToken;

    public TestCaseRunner(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Executes a Test Case and produces all Run Folder artifacts.
    /// Runner is the exclusive owner of the Case Run Folder.
    /// </summary>
    public async Task<TestCaseResult> ExecuteAsync(RunContext context)
    {
        var startTime = DateTime.UtcNow;
        var folderManager = new CaseRunFolderManager(context.RunsRoot);

        // Create Case Run Folder
        var caseRunFolder = folderManager.CreateRunFolder(context.RunId);
        var extractedRunId = Path.GetFileName(caseRunFolder);

        // Collect secret values for redaction
        var secretValues = context.EffectiveInputs
            .Where(kv => context.SecretInputs.TryGetValue(kv.Key, out var isSecret) && isSecret)
            .Select(kv => kv.Value?.ToString())
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();

        // Add secret env var values
        foreach (var envKey in context.SecretEnvVars)
        {
            if (context.EffectiveEnvironment.TryGetValue(envKey, out var envVal))
            {
                secretValues.Add(envVal);
            }
        }

        try
        {
            // Pre-node validation: workingDir containment
            var (workDirValid, resolvedWorkDir, workDirError) = folderManager.PrepareWorkingDir(
                caseRunFolder, context.WorkingDir);

            if (!workDirValid || resolvedWorkDir is null)
            {
                // Per spec section 7.2.1: fail with status=Error, error.type=RunnerError, don't start script
                var errorResult = CreateErrorResult(context, startTime,
                    ErrorType.RunnerError, workDirError ?? "workingDir validation failed");
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues);
                return errorResult;
            }

            // Write manifest snapshot
            var manifestSnapshot = CreateManifestSnapshot(context);
            await folderManager.WriteManifestSnapshotAsync(caseRunFolder, manifestSnapshot, context.SecretInputs);

            // Write params.json
            await folderManager.WriteParamsAsync(caseRunFolder, context.EffectiveInputs, context.SecretInputs);

            // Write env.json
            var envSnapshot = CreateEnvSnapshot();
            await folderManager.WriteEnvSnapshotAsync(caseRunFolder, envSnapshot);

            // Log secret on command line warning per spec section 7.4
            if (context.SecretInputs.Any(kv => kv.Value))
            {
                await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = ErrorCodes.EnvRefSecretOnCommandLine,
                    Level = "warning",
                    Message = "Secret values are being passed via command-line arguments",
                    Data = new Dictionary<string, object?>
                    {
                        ["parameters"] = context.SecretInputs.Where(kv => kv.Value).Select(kv => kv.Key).ToList()
                    }
                });
            }

            // Find script path
            var scriptPath = Path.Combine(context.TestCasePath, "run.ps1");
            if (!File.Exists(scriptPath))
            {
                var errorResult = CreateErrorResult(context, startTime,
                    ErrorType.RunnerError, $"Script not found: {scriptPath}");
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues);
                return errorResult;
            }

            // Execute PowerShell script
            var executor = new PowerShellExecutor(_cancellationToken);
            var execResult = await executor.ExecuteAsync(
                scriptPath,
                context.EffectiveInputs,
                context.EffectiveEnvironment,
                resolvedWorkDir,
                context.TimeoutSec,
                context.SecretInputs);

            // Write stdout/stderr (redacted)
            await folderManager.WriteStdoutAsync(caseRunFolder, execResult.Stdout, secretValues);
            await folderManager.WriteStderrAsync(caseRunFolder, execResult.Stderr, secretValues);

            // Build result
            var endTime = DateTime.UtcNow;
            var result = new TestCaseResult
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestCase,
                TestId = context.Manifest.Id,
                TestVersion = context.Manifest.Version,
                Status = execResult.Status,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ExitCode = execResult.ExitCode,
                EffectiveInputs = context.EffectiveInputs,
                Error = execResult.Error,
                Runner = new RunnerInfo
                {
                    Version = RunnerVersion,
                    PwshVersion = execResult.PwshVersion
                }
            };

            // Set suite/plan context per spec section 13.2
            if (!context.IsStandalone)
            {
                result.NodeId = context.NodeId;
                result.SuiteId = context.SuiteId;
                result.SuiteVersion = context.SuiteVersion;
                result.PlanId = context.PlanId;
                result.PlanVersion = context.PlanVersion;
            }

            // Write result.json (redacted)
            await folderManager.WriteResultAsync(caseRunFolder, result, context.SecretInputs);

            return result;
        }
        catch (Exception ex)
        {
            var errorResult = CreateErrorResult(context, startTime,
                ErrorType.RunnerError, ex.Message);
            await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues);
            return errorResult;
        }
    }

    private static CaseManifestSnapshot CreateManifestSnapshot(RunContext context)
    {
        return new CaseManifestSnapshot
        {
            SourceManifest = context.Manifest,
            ResolvedRef = context.TestCasePath,
            ResolvedIdentity = new IdentityInfo
            {
                Id = context.Manifest.Id,
                Version = context.Manifest.Version
            },
            EffectiveEnvironment = context.EffectiveEnvironment,
            EffectiveInputs = context.EffectiveInputs,
            InputTemplates = context.InputTemplates,
            ResolvedAt = DateTime.UtcNow.ToString("o"),
            EngineVersion = "1.0.0"
        };
    }

    private static EnvironmentSnapshot CreateEnvSnapshot()
    {
        var isElevated = false;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        return new EnvironmentSnapshot
        {
            OsVersion = Environment.OSVersion.ToString(),
            RunnerVersion = RunnerVersion,
            PwshVersion = "", // Will be filled by executor
            IsElevated = isElevated
        };
    }

    private static TestCaseResult CreateErrorResult(
        RunContext context,
        DateTime startTime,
        ErrorType errorType,
        string message)
    {
        var endTime = DateTime.UtcNow;
        var result = new TestCaseResult
        {
            SchemaVersion = "1.5.0",
            RunType = RunType.TestCase,
            TestId = context.Manifest.Id,
            TestVersion = context.Manifest.Version,
            Status = errorType == ErrorType.Timeout ? RunStatus.Timeout :
                     errorType == ErrorType.Aborted ? RunStatus.Aborted : RunStatus.Error,
            StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EffectiveInputs = context.EffectiveInputs,
            Error = new ErrorInfo
            {
                Type = errorType,
                Source = "Runner",
                Message = message
            },
            Runner = new RunnerInfo { Version = RunnerVersion }
        };

        if (!context.IsStandalone)
        {
            result.NodeId = context.NodeId;
            result.SuiteId = context.SuiteId;
            result.SuiteVersion = context.SuiteVersion;
            result.PlanId = context.PlanId;
            result.PlanVersion = context.PlanVersion;
        }

        return result;
    }

    private static async Task WriteArtifactsAsync(
        CaseRunFolderManager folderManager,
        string caseRunFolder,
        RunContext context,
        TestCaseResult result,
        List<string> secretValues)
    {
        try
        {
            var manifestSnapshot = CreateManifestSnapshot(context);
            await folderManager.WriteManifestSnapshotAsync(caseRunFolder, manifestSnapshot, context.SecretInputs);
            await folderManager.WriteParamsAsync(caseRunFolder, context.EffectiveInputs, context.SecretInputs);
            await folderManager.WriteEnvSnapshotAsync(caseRunFolder, CreateEnvSnapshot());
            await folderManager.WriteStdoutAsync(caseRunFolder, "", secretValues);
            await folderManager.WriteStderrAsync(caseRunFolder, "", secretValues);
            await folderManager.WriteResultAsync(caseRunFolder, result, context.SecretInputs);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Generates a unique run ID.
    /// </summary>
    public static string GenerateRunId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"R-{timestamp}-{shortGuid}";
    }
}
