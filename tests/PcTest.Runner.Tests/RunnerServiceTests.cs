using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerServiceTests
{
    [Fact]
    public void Runner_StatusMapping_MatchesSpec()
    {
        var root = RunnerTestHelpers.CreateTempDirectory();
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(runsRoot);

        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "ExitCodes",
            Name = "ExitCodes",
            Category = "Test",
            Version = "1.0.0"
        };

        var script = "param([int]$Code) exit $Code";
        var (manifestPath, _) = RunnerTestHelpers.WriteTestCase(root, script, manifest);

        var runner = new RunnerService();
        var requestOk = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?> { ["Code"] = 0 },
            new Dictionary<string, object?> { ["Code"] = 0 },
            Array.Empty<string>());
        var okResult = runner.RunTestCase(requestOk);
        Assert.Equal("Passed", okResult.Status);

        var requestFail = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?> { ["Code"] = 1 },
            new Dictionary<string, object?> { ["Code"] = 1 },
            Array.Empty<string>());
        var failResult = runner.RunTestCase(requestFail);
        Assert.Equal("Failed", failResult.Status);

        var requestError = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?> { ["Code"] = 2 },
            new Dictionary<string, object?> { ["Code"] = 2 },
            Array.Empty<string>());
        var errorResult = runner.RunTestCase(requestError);
        Assert.Equal("Error", errorResult.Status);
        Assert.Equal("ScriptError", errorResult.ErrorType);
    }

    [Fact]
    public void Runner_SecretRedaction_AppliesToArtifacts()
    {
        var root = RunnerTestHelpers.CreateTempDirectory();
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(runsRoot);

        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Secret",
            Name = "Secret",
            Category = "Test",
            Version = "1.0.0"
        };

        var script = "param([string]$SecretValue) Write-Output \"$SecretValue\"; exit 0";
        var (manifestPath, _) = RunnerTestHelpers.WriteTestCase(root, script, manifest);

        var runner = new RunnerService();
        var request = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?> { ["SecretValue"] = "super-secret" },
            new Dictionary<string, object?> { ["SecretValue"] = "***" },
            new[] { "SecretValue" });
        var result = runner.RunTestCase(request);

        var runFolder = Path.Combine(runsRoot, result.RunId);
        var manifestJson = File.ReadAllText(Path.Combine(runFolder, "manifest.json"));
        var paramsJson = File.ReadAllText(Path.Combine(runFolder, "params.json"));
        var resultJson = File.ReadAllText(Path.Combine(runFolder, "result.json"));
        var stdout = File.ReadAllText(Path.Combine(runFolder, "stdout.txt"));
        var events = File.ReadAllText(Path.Combine(runFolder, "events.jsonl"));

        Assert.Contains("\"SecretValue\": \"***\"", manifestJson);
        Assert.Contains("\"SecretValue\": \"***\"", paramsJson);
        Assert.Contains("\"SecretValue\": \"***\"", resultJson);
        Assert.DoesNotContain("super-secret", stdout);
        Assert.Contains("EnvRef.SecretOnCommandLine", events);
    }

    [Fact]
    public void Runner_WorkingDirContainment_RejectsEscape()
    {
        var root = RunnerTestHelpers.CreateTempDirectory();
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(runsRoot);

        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "WorkDir",
            Name = "WorkDir",
            Category = "Test",
            Version = "1.0.0"
        };

        var script = "exit 0";
        var (manifestPath, _) = RunnerTestHelpers.WriteTestCase(root, script, manifest);

        var request = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            Array.Empty<string>());
        request = request with { WorkingDir = ".." };

        var runner = new RunnerService();
        var result = runner.RunTestCase(request);
        Assert.Equal("Error", result.Status);
        Assert.Equal("RunnerError", result.ErrorType);
    }

    [Fact]
    public void Runner_PreNodeValidation_RejectsMissingFileInput()
    {
        var root = RunnerTestHelpers.CreateTempDirectory();
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(runsRoot);

        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "FileCheck",
            Name = "FileCheck",
            Category = "Test",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "InputFile", Type = "file", Required = true }
            }
        };

        var script = "exit 0";
        var (manifestPath, _) = RunnerTestHelpers.WriteTestCase(root, script, manifest);
        var request = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?> { ["InputFile"] = "missing.txt" },
            new Dictionary<string, object?> { ["InputFile"] = "missing.txt" },
            Array.Empty<string>());

        var runner = new RunnerService();
        var result = runner.RunTestCase(request);
        Assert.Equal("Error", result.Status);
        Assert.Equal("RunnerError", result.ErrorType);
    }
}
