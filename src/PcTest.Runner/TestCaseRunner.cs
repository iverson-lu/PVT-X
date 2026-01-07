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
        using var folderManager = new CaseRunFolderManager(context.RunsRoot);

        // Create or reuse Case Run Folder
        var caseRunFolder = context.ExistingRunFolder ?? folderManager.CreateRunFolder(context.RunId);
        if (context.ExistingRunFolder is not null)
        {
            Directory.CreateDirectory(caseRunFolder);
            Directory.CreateDirectory(Path.Combine(caseRunFolder, "artifacts"));
        }
        var extractedRunId = Path.GetFileName(caseRunFolder);

        // Create control inbox directory for reboot requests
        var controlDir = RebootResumeManager.GetControlDir(caseRunFolder);
        Directory.CreateDirectory(controlDir);

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

        // Inject PVT-X standard environment variables (before try block so it's available in catch)
        var enhancedEnvironment = new Dictionary<string, string>(context.EffectiveEnvironment)
        {
            ["PVTX_TESTCASE_PATH"] = context.TestCasePath,
            ["PVTX_TESTCASE_NAME"] = context.Manifest.Name,
            ["PVTX_TESTCASE_ID"] = context.Manifest.Id,
            ["PVTX_TESTCASE_VER"] = context.Manifest.Version,
            ["PVTX_RUN_ID"] = extractedRunId,
            ["PVTX_PHASE"] = context.Phase.ToString(),
            ["PVTX_CONTROL_DIR"] = controlDir
        };

        // Add PVT-X PowerShell modules to PSModulePath (enables module autoload for common case helpers)
        var assetsRoot = context.AssetsRoot;
        if (string.IsNullOrWhiteSpace(assetsRoot))
            throw new InvalidOperationException("AssetsRoot must be provided in RunContext");

        var modulesRoot = Path.Combine(assetsRoot, "PowerShell", "Modules");
        enhancedEnvironment["PVTX_ASSETS_ROOT"] = assetsRoot;
        enhancedEnvironment["PVTX_MODULES_ROOT"] = modulesRoot;

        // Only add to PSModulePath if the directory exists (may not exist in test scenarios)
        if (Directory.Exists(modulesRoot))
        {
            var psModulePath = Environment.GetEnvironmentVariable("PSModulePath");
            enhancedEnvironment["PSModulePath"] =
                string.IsNullOrEmpty(psModulePath) ? modulesRoot : modulesRoot + ";" + psModulePath;
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
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues, enhancedEnvironment);
                return errorResult;
            }

            if (!context.IsResume)
            {
                // Write manifest snapshot
                var manifestSnapshot = CreateManifestSnapshot(context, enhancedEnvironment);
                await folderManager.WriteManifestSnapshotAsync(caseRunFolder, manifestSnapshot, context.SecretInputs);

                // Write params.json
                await folderManager.WriteParamsAsync(caseRunFolder, context.EffectiveInputs, context.SecretInputs);
            }

            // Record test started event
            await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Code = "TestCase.Started",
                Level = "info",
                Message = $"Test case '{context.Manifest.Id}' (version {context.Manifest.Version}) execution started",
                Data = new Dictionary<string, object?>
                {
                    ["testId"] = context.Manifest.Id,
                    ["testVersion"] = context.Manifest.Version,
                    ["runId"] = extractedRunId,
                    ["isStandalone"] = context.IsStandalone,
                    ["nodeId"] = context.IsStandalone ? null : context.NodeId,
                    ["phase"] = context.Phase,
                    ["isResume"] = context.IsResume
                }
            });

            if (!context.IsResume)
            {
                // Write env.json
                var envSnapshot = CreateEnvSnapshot();
                await folderManager.WriteEnvSnapshotAsync(caseRunFolder, envSnapshot);
            }

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
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues, enhancedEnvironment);
                return errorResult;
            }

            // Execute PowerShell script with streaming output
            var executor = new PowerShellExecutor(_cancellationToken);

            // Create output handler for real-time streaming to log files
            async Task OnOutputLine(string? line, bool isStderr)
            {
                if (line is null) return;
                if (isStderr)
                {
                    await folderManager.AppendStderrLineAsync(caseRunFolder, line, secretValues);
                }
                else
                {
                    await folderManager.AppendStdoutLineAsync(caseRunFolder, line, secretValues);
                }
            }

            var execResult = await executor.ExecuteAsync(
                scriptPath,
                context.EffectiveInputs,
                enhancedEnvironment,
                resolvedWorkDir,
                context.TimeoutSec,
                context.SecretInputs,
                OnOutputLine);

            // Flush and close the streaming writers
            folderManager.FlushAndCloseWriters(caseRunFolder);

            // Detect reboot request after case exit
            var rebootRequest = RebootResumeManager.ReadRebootRequest(controlDir, out var rebootError);
            if (!string.IsNullOrWhiteSpace(rebootError))
            {
                var errorResult = CreateErrorResult(context, startTime,
                    ErrorType.RunnerError, rebootError);
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues, enhancedEnvironment);
                return errorResult;
            }

            if (rebootRequest is not null)
            {
                if (context.IsResume)
                {
                    var errorResult = CreateErrorResult(context, startTime,
                        ErrorType.RunnerError, "Reboot request detected during resume; multiple reboots are not allowed.");
                    await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues, enhancedEnvironment);
                    return errorResult;
                }

                var session = new ResumeSession
                {
                    RunId = extractedRunId,
                    EntityType = "TestCase",
                    EntityId = $"{context.Manifest.Id}@{context.Manifest.Version}",
                    CurrentCaseId = context.Manifest.Id,
                    NextPhase = rebootRequest.NextPhase,
                    ResumeToken = RebootResumeManager.CreateResumeToken(),
                    ResumeCount = 0,
                    State = "PendingResume",
                    TestCasePath = context.TestCasePath,
                    Manifest = context.Manifest,
                    EffectiveInputs = context.EffectiveInputs,
                    EffectiveEnvironment = context.EffectiveEnvironment,
                    SecretInputs = context.SecretInputs,
                    SecretEnvVars = context.SecretEnvVars,
                    InputTemplates = context.InputTemplates,
                    WorkingDir = context.WorkingDir,
                    TimeoutSec = context.TimeoutSec,
                    AssetsRoot = context.AssetsRoot,
                    NodeId = context.NodeId,
                    SuiteId = context.SuiteId,
                    SuiteVersion = context.SuiteVersion,
                    PlanId = context.PlanId,
                    PlanVersion = context.PlanVersion,
                    ParentRunId = context.ParentRunId
                };

                await RebootResumeManager.SaveSessionAsync(caseRunFolder, session);
                await RebootResumeManager.CreateResumeTaskAsync(extractedRunId, session.ResumeToken, context.RunsRoot);

                await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestCase.RebootRequested",
                    Level = "info",
                    Message = $"Reboot requested by test case '{context.Manifest.Id}'",
                    Data = new Dictionary<string, object?>
                    {
                        ["testId"] = context.Manifest.Id,
                        ["nextPhase"] = rebootRequest.NextPhase,
                        ["reason"] = rebootRequest.Reason,
                        ["delaySec"] = rebootRequest.DelaySec
                    }
                });

                await RebootResumeManager.RequestRebootAsync(rebootRequest.DelaySec);

                var rebootErrorResult = CreateErrorResult(context, startTime,
                    ErrorType.RunnerError, "Reboot was requested but the system did not reboot.");
                await WriteArtifactsAsync(folderManager, caseRunFolder, context, rebootErrorResult, secretValues, enhancedEnvironment);
                return rebootErrorResult;
            }

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

            // Record test completed event
            await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Code = "TestCase.Completed",
                Level = result.Status == RunStatus.Passed ? "info" : "warning",
                Message = $"Test case '{context.Manifest.Id}' execution completed with status: {result.Status}",
                Data = new Dictionary<string, object?>
                {
                    ["testId"] = context.Manifest.Id,
                    ["testVersion"] = context.Manifest.Version,
                    ["runId"] = extractedRunId,
                    ["status"] = result.Status.ToString(),
                    ["exitCode"] = result.ExitCode,
                    ["duration"] = (endTime - startTime).TotalSeconds
                }
            });

            await FinalizeResumeAsync(caseRunFolder, extractedRunId);

            return result;
        }
        catch (Exception ex)
        {
            var errorResult = CreateErrorResult(context, startTime,
                ErrorType.RunnerError, ex.Message);
            await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues);

            // Record test error event
            await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Code = "TestCase.Error",
                Level = "error",
                Message = $"Test case '{context.Manifest.Id}' execution failed with exception: {ex.Message}",
                Data = new Dictionary<string, object?>
                {
                    ["testId"] = context.Manifest.Id,
                    ["testVersion"] = context.Manifest.Version,
                    ["runId"] = extractedRunId,
                    ["errorType"] = ex.GetType().Name,
                    ["errorMessage"] = ex.Message
                }
            });

            await FinalizeResumeAsync(caseRunFolder, extractedRunId);

            return errorResult;
        }
    }

    private static async Task FinalizeResumeAsync(string caseRunFolder, string runId)
    {
        if (!RebootResumeManager.TryLoadSession(caseRunFolder, out var session) || session is null)
        {
            return;
        }

        session.State = "Finalized";
        await RebootResumeManager.SaveSessionAsync(caseRunFolder, session);

        var controlDir = RebootResumeManager.GetControlDir(caseRunFolder);
        var rebootPath = RebootResumeManager.GetRebootRequestPath(controlDir);
        if (File.Exists(rebootPath))
        {
            File.Delete(rebootPath);
        }

        await RebootResumeManager.DeleteResumeTaskAsync(runId);
    }

    private static CaseManifestSnapshot CreateManifestSnapshot(RunContext context, Dictionary<string, string> enhancedEnvironment)
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
            EffectiveEnvironment = enhancedEnvironment,
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
        List<string> secretValues,
        Dictionary<string, string>? enhancedEnvironment = null)
    {
        try
        {
            var envToUse = enhancedEnvironment ?? context.EffectiveEnvironment;
            var manifestSnapshot = CreateManifestSnapshot(context, envToUse);
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
