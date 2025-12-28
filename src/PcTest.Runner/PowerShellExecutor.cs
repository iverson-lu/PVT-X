using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;

namespace PcTest.Runner;

/// <summary>
/// PowerShell executor that manages process lifecycle.
/// Per spec section 11.
/// </summary>
public sealed class PowerShellExecutor
{
    private const string PwshExecutable = "pwsh.exe";
    private static readonly Version MinPwshVersion = new(7, 0);

    /// <summary>
    /// Result of PowerShell execution.
    /// </summary>
    public sealed class ExecutionResult
    {
        public int? ExitCode { get; init; }
        public RunStatus Status { get; init; }
        public ErrorInfo? Error { get; init; }
        public string Stdout { get; init; } = string.Empty;
        public string Stderr { get; init; } = string.Empty;
        public TimeSpan Duration { get; init; }
        public string? PwshVersion { get; init; }
        public bool WasTimeout { get; init; }
        public bool WasAborted { get; init; }
    }

    private readonly CancellationToken _cancellationToken;

    public PowerShellExecutor(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Discovers and validates pwsh.exe.
    /// </summary>
    public (bool Success, string? PwshPath, string? Version, string? Error) DiscoverPwsh()
    {
        try
        {
            // Try to find pwsh in PATH
            var startInfo = new ProcessStartInfo
            {
                FileName = PwshExecutable,
                Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, null, null, "Failed to start pwsh.exe");
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                return (false, null, null, "pwsh.exe returned non-zero exit code");
            }

            // Parse version
            if (Version.TryParse(output, out var version))
            {
                if (version < MinPwshVersion)
                {
                    return (false, null, output, $"PowerShell version {version} is below minimum required {MinPwshVersion}");
                }
                return (true, PwshExecutable, output, null);
            }

            return (false, null, output, $"Could not parse PowerShell version: {output}");
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Failed to discover pwsh.exe: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a PowerShell script with the given parameters.
    /// Per spec section 9 and 11.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(
        string scriptPath,
        Dictionary<string, object?> parameters,
        Dictionary<string, string> environment,
        string workingDirectory,
        int? timeoutSec,
        Dictionary<string, bool>? secretParams = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        // Discover pwsh first
        var (pwshFound, pwshPath, pwshVersion, pwshError) = DiscoverPwsh();
        if (!pwshFound || pwshPath is null)
        {
            return new ExecutionResult
            {
                Status = RunStatus.Error,
                Error = new ErrorInfo
                {
                    Type = ErrorType.RunnerError,
                    Source = "Runner",
                    Message = pwshError ?? "PowerShell not found"
                },
                Duration = stopwatch.Elapsed
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pwshPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        // Build argument list per spec section 9
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        // Add parameters per spec section 9
        foreach (var (name, value) in parameters)
        {
            if (value is null)
                continue; // Missing optional parameters MUST be omitted

            startInfo.ArgumentList.Add($"-{name}");
            AddParameterValue(startInfo.ArgumentList, value);
        }

        // Set environment variables
        startInfo.Environment.Clear();
        foreach (var (key, val) in environment)
        {
            startInfo.Environment[key] = val;
        }

        // Add system environment variables that might be needed
        foreach (var key in new[] { "PATH", "SYSTEMROOT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH" })
        {
            var sysVal = Environment.GetEnvironmentVariable(key);
            if (sysVal is not null && !startInfo.Environment.ContainsKey(key))
            {
                startInfo.Environment[key] = sysVal;
            }
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new ExecutionResult
                {
                    Status = RunStatus.Error,
                    Error = new ErrorInfo
                    {
                        Type = ErrorType.RunnerError,
                        Source = "Runner",
                        Message = "Failed to start PowerShell process"
                    },
                    Duration = stopwatch.Elapsed,
                    PwshVersion = pwshVersion
                };
            }

            // Read output asynchronously
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Wait with timeout
            var timeout = timeoutSec.HasValue
                ? TimeSpan.FromSeconds(timeoutSec.Value)
                : TimeSpan.FromDays(1); // Effectively no timeout

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cancellationToken);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Kill process tree
                KillProcessTree(process);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (_cancellationToken.IsCancellationRequested)
                {
                    return new ExecutionResult
                    {
                        Status = RunStatus.Aborted,
                        Error = new ErrorInfo
                        {
                            Type = ErrorType.Aborted,
                            Source = "Runner",
                            Message = "Execution was aborted by user"
                        },
                        Stdout = stdout,
                        Stderr = stderr,
                        Duration = stopwatch.Elapsed,
                        PwshVersion = pwshVersion,
                        WasAborted = true
                    };
                }

                return new ExecutionResult
                {
                    Status = RunStatus.Timeout,
                    Error = new ErrorInfo
                    {
                        Type = ErrorType.Timeout,
                        Source = "Runner",
                        Message = $"Execution timed out after {timeoutSec} seconds"
                    },
                    Stdout = stdout,
                    Stderr = stderr,
                    Duration = stopwatch.Elapsed,
                    PwshVersion = pwshVersion,
                    WasTimeout = true
                };
            }

            var stdoutResult = await stdoutTask;
            var stderrResult = await stderrTask;

            // Map exit code per spec section 11.2
            var exitCode = process.ExitCode;
            var status = MapExitCode(exitCode);

            ErrorInfo? error = null;
            if (status == RunStatus.Error)
            {
                error = new ErrorInfo
                {
                    Type = ErrorType.ScriptError,
                    Source = "Script",
                    Message = $"Script exited with code {exitCode}"
                };
            }

            return new ExecutionResult
            {
                ExitCode = exitCode,
                Status = status,
                Error = error,
                Stdout = stdoutResult,
                Stderr = stderrResult,
                Duration = stopwatch.Elapsed,
                PwshVersion = pwshVersion
            };
        }
        catch (Exception ex)
        {
            if (process is not null && !process.HasExited)
            {
                KillProcessTree(process);
            }

            return new ExecutionResult
            {
                Status = RunStatus.Error,
                Error = new ErrorInfo
                {
                    Type = ErrorType.RunnerError,
                    Source = "Runner",
                    Message = ex.Message,
                    Stack = ex.StackTrace
                },
                Stdout = stdoutBuilder.ToString(),
                Stderr = stderrBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                PwshVersion = pwshVersion
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Adds parameter value(s) to argument list per spec section 9.
    /// </summary>
    private static void AddParameterValue(ICollection<string> argumentList, object value)
    {
        switch (value)
        {
            case bool b:
                // Boolean values: $true / $false
                argumentList.Add(b ? "$true" : "$false");
                break;

            case int i:
                argumentList.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case double d:
                argumentList.Add(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case string s:
                argumentList.Add(s);
                break;

            case int[] intArray:
                foreach (var item in intArray)
                {
                    argumentList.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                break;

            case double[] doubleArray:
                foreach (var item in doubleArray)
                {
                    argumentList.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                break;

            case string[] stringArray:
                // Array parameters as repeated values: -Modes "A" "B"
                foreach (var item in stringArray)
                {
                    argumentList.Add(item);
                }
                break;

            case bool[] boolArray:
                foreach (var item in boolArray)
                {
                    argumentList.Add(item ? "$true" : "$false");
                }
                break;

            case object[] objArray:
                foreach (var item in objArray)
                {
                    if (item is not null)
                    {
                        AddParameterValue(argumentList, item);
                    }
                }
                break;

            default:
                argumentList.Add(value.ToString() ?? "");
                break;
        }
    }

    /// <summary>
    /// Maps exit code to RunStatus per spec section 11.2.
    /// </summary>
    private static RunStatus MapExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => RunStatus.Passed,
            1 => RunStatus.Failed,
            _ => RunStatus.Error
        };
    }

    /// <summary>
    /// Kills the entire process tree.
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore
            }
        }
    }
}
