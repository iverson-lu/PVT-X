using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void PowerShellArguments_AreSerializedCorrectly()
    {
        using var temp = new TempWorkspace();
        var processRunner = new CapturingProcessRunner();
        var runner = new RunnerService(processRunner, validatePowerShell: false);

        var testCase = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Flag", Type = ParameterType.Boolean, Required = true },
                new ParameterDefinition { Name = "Modes", Type = ParameterType.StringArray, Required = true }
            }
        };

        var request = new RunnerRequest(
            testCase,
            temp.ManifestPath,
            new Dictionary<string, object>
            {
                ["Flag"] = true,
                ["Modes"] = new[] { "A", "B" }
            },
            new Dictionary<string, object>
            {
                ["Flag"] = true,
                ["Modes"] = new[] { "A", "B" }
            },
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
            null,
            "node-1",
            new Identity("Suite", "1.0"),
            null,
            new CaseRunManifestSnapshot(testCase, temp.CaseFolder, new Identity("Case", "1.0"), new Dictionary<string, string>(), new Dictionary<string, object>(), new Dictionary<string, object>(), "Engine"),
            temp.RunsRoot,
            "G-1",
            null);

        runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(processRunner.LastStartInfo);
        var args = processRunner.LastStartInfo!.ArgumentList.ToArray();
        Assert.Contains("-Flag", args);
        Assert.Contains("$true", args);
        int modesIndex = Array.IndexOf(args, "-Modes");
        Assert.True(modesIndex >= 0);
        Assert.Equal("A", args[modesIndex + 1]);
        Assert.Equal("B", args[modesIndex + 2]);
    }

    [Fact]
    public void StatusMapping_ExitCodes_AreHandled()
    {
        using var temp = new TempWorkspace();
        var processRunner = new CapturingProcessRunner { NextResult = new ProcessRunResult(1, string.Empty, string.Empty, false) };
        var runner = new RunnerService(processRunner, validatePowerShell: false);
        var request = temp.CreateRunnerRequest();
        RunnerResult result = runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("Failed", result.Status);

        processRunner.NextResult = new ProcessRunResult(2, string.Empty, string.Empty, false);
        result = runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("Error", result.Status);

        processRunner.NextResult = new ProcessRunResult(0, string.Empty, string.Empty, true);
        result = runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("Timeout", result.Status);
    }

    [Fact]
    public void SecretInputs_AreRedacted()
    {
        using var temp = new TempWorkspace();
        var processRunner = new CapturingProcessRunner();
        var runner = new RunnerService(processRunner, validatePowerShell: false);
        var request = temp.CreateRunnerRequest(secret: true);
        runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        string resultPath = Path.Combine(temp.RunsRoot, Directory.GetDirectories(temp.RunsRoot).First(), "result.json");
        var result = JsonUtilities.ReadJsonFile<TestCaseResult>(resultPath);
        Assert.Equal("***", result.EffectiveInputs["Secret"]);
    }

    [Fact]
    public void RunFolderLayout_IsCreated()
    {
        using var temp = new TempWorkspace();
        var runner = new RunnerService(new CapturingProcessRunner(), validatePowerShell: false);
        var request = temp.CreateRunnerRequest();
        RunnerResult result = runner.RunTestCaseAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        string runFolder = Path.Combine(temp.RunsRoot, result.RunId);
        Assert.True(File.Exists(Path.Combine(runFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "result.json")));
    }
}

internal sealed class CapturingProcessRunner : IProcessRunner
{
    public ProcessStartInfo? LastStartInfo { get; private set; }
    public ProcessRunResult NextResult { get; set; } = new ProcessRunResult(0, string.Empty, string.Empty, false);

    public Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        LastStartInfo = startInfo;
        return Task.FromResult(NextResult);
    }
}

internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "pctest-runner-" + Guid.NewGuid().ToString("N"));
        RunsRoot = Path.Combine(Root, "Runs");
        CaseFolder = Path.Combine(Root, "Case");
        Directory.CreateDirectory(RunsRoot);
        Directory.CreateDirectory(CaseFolder);
        File.WriteAllText(Path.Combine(CaseFolder, "run.ps1"), "Write-Output 'ok'");
        ManifestPath = Path.Combine(CaseFolder, "test.manifest.json");
        var manifest = new
        {
            schemaVersion = "1.5.0",
            id = "Case",
            name = "Case",
            category = "Cat",
            version = "1.0.0",
            parameters = new[]
            {
                new { name = "Value", type = "int", required = true, @default = 1 },
                new { name = "Secret", type = "string", required = false }
            }
        };
        JsonUtilities.WriteJsonFile(ManifestPath, manifest);
    }

    public string Root { get; }
    public string RunsRoot { get; }
    public string CaseFolder { get; }
    public string ManifestPath { get; }

    public RunnerRequest CreateRunnerRequest(bool secret = false)
    {
        var testCase = JsonUtilities.ReadJsonFile<TestCaseManifest>(ManifestPath);
        var effectiveInputs = new Dictionary<string, object>
        {
            ["Value"] = 1,
            ["Secret"] = "top"
        };
        var redacted = new Dictionary<string, object>
        {
            ["Value"] = 1,
            ["Secret"] = secret ? "***" : "top"
        };
        var secretInputs = new Dictionary<string, bool> { ["Secret"] = secret };
        return new RunnerRequest(
            testCase,
            ManifestPath,
            effectiveInputs,
            redacted,
            secretInputs,
            new Dictionary<string, string>(),
            null,
            null,
            null,
            null,
            new CaseRunManifestSnapshot(testCase, CaseFolder, new Identity("Case", "1.0"), new Dictionary<string, string>(), redacted, new Dictionary<string, object>(), "Engine"),
            RunsRoot,
            null,
            null);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
