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
        var caseRunFolder = string.IsNullOrWhiteSpace(context.RunFolderPath)
            ? folderManager.CreateRunFolder(context.RunId)
            : PathUtils.NormalizePath(context.RunFolderPath);
        var extractedRunId = Path.GetFileName(caseRunFolder);
        Directory.CreateDirectory(caseRunFolder);
        Directory.CreateDirectory(Path.Combine(caseRunFolder, "artifacts"));
        var controlDir = RebootResumeManager.GetControlDir(caseRunFolder);
        Directory.CreateDirectory(controlDir);
        var controlFile = Path.Combine(controlDir, RebootResumeManager.ControlFileName);
        if (File.Exists(controlFile))
        {
            File.Delete(controlFile);
        }

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
            ["PVTX_RUN_ID"] = context.RunId,
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

            // Write manifest snapshot
            var manifestSnapshot = CreateManifestSnapshot(context, enhancedEnvironment);
            await folderManager.WriteManifestSnapshotAsync(caseRunFolder, manifestSnapshot, context.SecretInputs);

            // Write params.json
            await folderManager.WriteParamsAsync(caseRunFolder, context.EffectiveInputs, context.SecretInputs);

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
                    ["nodeId"] = context.IsStandalone ? null : context.NodeId
                }
            });

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
                    await folderManager.AppendStderrLineAsync(caseRunFolder, line, secretValues, context.AppendOutput);
                }
                else
                {
                    await folderManager.AppendStdoutLineAsync(caseRunFolder, line, secretValues, context.AppendOutput);
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

            if (RebootResumeManager.TryReadRebootRequest(controlDir, out var rebootRequest, out var rebootError))
            {
                if (!string.IsNullOrEmpty(rebootError))
                {
                    var errorResult = CreateErrorResult(context, startTime,
                        ErrorType.RunnerError, rebootError);
                    await WriteArtifactsAsync(folderManager, caseRunFolder, context, errorResult, secretValues, enhancedEnvironment);
                    return errorResult;
                }

                await folderManager.AppendEventAsync(caseRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestCase.RebootRequested",
                    Level = "info",
                    Message = $"Reboot requested by test case for next phase {rebootRequest!.NextPhase}",
                    Data = new Dictionary<string, object?>
                    {
                        ["runId"] = extractedRunId,
                        ["nextPhase"] = rebootRequest.NextPhase,
                        ["reason"] = rebootRequest.Reason
                    }
                });

                var resumeToken = Guid.NewGuid().ToString("N");
                var session = new RebootResumeSession
                {
                    RunId = context.RunId,
                    EntityType = context.RunEntityType ?? "TestCase",
                    EntityId = context.RunEntityId ?? context.Manifest.Identity,
                    CurrentCaseId = context.Manifest.Id,
                    NextPhase = rebootRequest.NextPhase,
                    ResumeToken = resumeToken,
                    ResumeCount = 0,
                    State = "PendingResume",
                    RunFolder = caseRunFolder,
                    RunsRoot = context.RunsRoot,
                    CasesRoot = context.CasesRoot,
                    SuitesRoot = context.SuitesRoot,
                    PlansRoot = context.PlansRoot,
                    CaseInputs = context.CaseInputs,
                    EnvironmentOverrides = context.EnvironmentOverrides
                };

                RebootResumeManager.SaveSession(session);

                var runnerPath = Environment.ProcessPath ?? throw new InvalidOperationException("Runner path not available");
                RebootResumeManager.CreateResumeTask(session, runnerPath);
                RebootResumeManager.RequestReboot(rebootRequest.Reboot?.DelaySec);
                throw new RebootRequestedException(rebootRequest);
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

            var sessionPath = RebootResumeManager.GetSessionPath(caseRunFolder);
            if (File.Exists(sessionPath))
            {
                var session = RebootResumeManager.LoadSessionForRunFolder(caseRunFolder);
                RebootResumeManager.FinalizeSession(session);
            }

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

            return result;
        }
        catch (RebootRequestedException)
        {
            throw;
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

            return errorResult;
        }
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
