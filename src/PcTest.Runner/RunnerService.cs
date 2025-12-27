using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerService
{
    public RunnerResult Run(RunnerRequest request, CancellationToken token)
    {
        Directory.CreateDirectory(request.CaseRunFolder);

        var resultPath = Path.Combine(request.CaseRunFolder, "result.json");
        var stdoutPath = Path.Combine(request.CaseRunFolder, "stdout.txt");
        var stderrPath = Path.Combine(request.CaseRunFolder, "stderr.txt");
        var paramsPath = Path.Combine(request.CaseRunFolder, "params.json");
        var manifestPath = Path.Combine(request.CaseRunFolder, "manifest.json");
        var eventsPath = Path.Combine(request.CaseRunFolder, "events.jsonl");

        File.Copy(request.ManifestPath, manifestPath, true);

        if (!IsContained(request.WorkingDir, request.CaseRunFolder))
        {
            var error = new RunnerResult
            {
                Status = "Error",
                Error = new ResultError
                {
                    Type = ErrorCodes.RunnerError,
                    Message = "WorkingDir out of containment"
                }
            };
            File.WriteAllText(resultPath, JsonSerializer.Serialize(error, JsonDefaults.Options));
            return error;
        }

        Directory.CreateDirectory(request.WorkingDir);

        var redactedInputs = RedactInputs(request.Inputs, request.Redaction.SecretInputs);
        File.WriteAllText(paramsPath, JsonSerializer.Serialize(redactedInputs, JsonDefaults.Options));

        var events = new List<object>
        {
            new { code = EventCodes.RunnerStarted, time = DateTimeOffset.UtcNow }
        };

        var pwshPath = "pwsh";
        var versionCheck = EnsurePwshVersion(pwshPath);
        if (!versionCheck)
        {
            var error = new RunnerResult
            {
                Status = "Error",
                Error = new ResultError
                {
                    Type = ErrorCodes.RunnerError,
                    Message = "pwsh.exe not found or version < 7.0"
                }
            };
            File.WriteAllText(resultPath, JsonSerializer.Serialize(error, JsonDefaults.Options));
            return error;
        }

        var psi = new ProcessStartInfo
        {
            FileName = pwshPath,
            WorkingDirectory = request.WorkingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(request.ScriptPath);

        foreach (var (key, value) in request.Inputs)
        {
            AppendArgument(psi, key, value);
            if (request.Redaction.SecretInputs.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                events.Add(new { code = WarningCodes.EnvRefSecretOnCommandLine, parameter = key, time = DateTimeOffset.UtcNow });
            }
        }

        foreach (var (key, value) in request.Environment)
        {
            psi.Environment[key] = value;
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("Process start failed.");
            }

            using var stdoutWriter = new StreamWriter(stdoutPath);
            using var stderrWriter = new StreamWriter(stderrPath);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            process.WaitForExit();
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            stdoutWriter.Write(RedactText(stdout, request.Redaction.SecretInputs));
            stderrWriter.Write(RedactText(stderr, request.Redaction.SecretInputs));

            var status = process.ExitCode switch
            {
                0 => "Passed",
                1 => "Failed",
                _ => "Error"
            };

            var runnerResult = new RunnerResult
            {
                Status = status,
                ExitCode = process.ExitCode,
                Error = status == "Error" ? new ResultError { Type = ErrorCodes.ScriptError } : null
            };

            events.Add(new { code = EventCodes.RunnerCompleted, time = DateTimeOffset.UtcNow, exitCode = process.ExitCode });
            WriteEvents(eventsPath, events);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(runnerResult, JsonDefaults.Options));
            return runnerResult;
        }
        catch (Exception ex)
        {
            var runnerResult = new RunnerResult
            {
                Status = "Error",
                Error = new ResultError { Type = ErrorCodes.RunnerError, Message = ex.Message }
            };

            WriteEvents(eventsPath, events);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(runnerResult, JsonDefaults.Options));
            return runnerResult;
        }
    }

    private static bool EnsurePwshVersion(string pwshPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pwshPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$PSVersionTable.PSVersion.Major");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (int.TryParse(output.Trim(), out var major))
            {
                return major >= 7;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendArgument(ProcessStartInfo psi, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is Array array)
        {
            foreach (var item in array)
            {
                AppendArgument(psi, name, item);
            }
            return;
        }

        psi.ArgumentList.Add($"-{name}");
        psi.ArgumentList.Add(value is bool boolean ? (boolean ? "$true" : "$false") : value.ToString() ?? string.Empty);
    }

    private static Dictionary<string, object?> RedactInputs(Dictionary<string, object?> inputs, List<string> secretInputs)
    {
        var redacted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in inputs)
        {
            redacted[key] = secretInputs.Contains(key, StringComparer.OrdinalIgnoreCase) ? "***" : value;
        }

        return redacted;
    }

    private static string RedactText(string text, List<string> secretInputs)
    {
        var output = text;
        foreach (var secret in secretInputs)
        {
            output = output.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
        }

        return output;
    }

    private static void WriteEvents(string path, IEnumerable<object> events)
    {
        using var writer = new StreamWriter(path);
        foreach (var entry in events)
        {
            writer.WriteLine(JsonSerializer.Serialize(entry, JsonDefaults.Options));
        }
    }

    private static bool IsContained(string child, string root)
    {
        var childFull = Path.GetFullPath(child);
        var rootFull = Path.GetFullPath(root);
        return childFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }
}
