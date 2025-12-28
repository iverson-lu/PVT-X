using System.Text.Json;
using PcTest.Contracts.Models;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void PowerShellArgumentBuilder_FormatsValues()
    {
        var args = PowerShellArgumentBuilder.BuildArgumentList(new Dictionary<string, object?>
        {
            ["DurationSec"] = 30,
            ["Enabled"] = true,
            ["Modes"] = new[] { "A", "B" }
        });

        Assert.Contains("-DurationSec", args);
        Assert.Contains("30", args);
        Assert.Contains("-Enabled", args);
        Assert.Contains("$true", args);
        Assert.Contains("-Modes", args);
        Assert.Contains("A", args);
        Assert.Contains("B", args);
    }

    [Fact]
    public void RunnerResultMapper_MapsExitCodes()
    {
        var baseResult = new TestCaseResult
        {
            TestId = "Case",
            TestVersion = "1.0.0",
            Status = RunStatus.Error,
            StartTime = DateTimeOffset.UtcNow.ToString("O"),
            EndTime = DateTimeOffset.UtcNow.ToString("O"),
            EffectiveInputs = new Dictionary<string, object?>()
        };

        Assert.Equal(RunStatus.Passed, RunnerResultMapper.MapExitCode(baseResult, 0).Status);
        Assert.Equal(RunStatus.Failed, RunnerResultMapper.MapExitCode(baseResult, 1).Status);
        var errorResult = RunnerResultMapper.MapExitCode(baseResult, 3);
        Assert.Equal(RunStatus.Error, errorResult.Status);
        Assert.Equal("ScriptError", errorResult.Error?.Type);
    }

    [Fact]
    public async Task Runner_RejectsWorkingDirOutOfRunFolder()
    {
        using var temp = new TempRoot();
        var runsRoot = temp.CreateDirectory("runs");
        var caseDir = temp.CreateDirectory("case");
        File.WriteAllText(Path.Combine(caseDir, "run.ps1"), "exit 0");

        var runner = new TestCaseRunner();
        var request = new RunnerRequest
        {
            RunsRoot = runsRoot,
            TestCasePath = caseDir,
            Manifest = new TestCaseManifest
            {
                SchemaVersion = "1.5.0",
                Id = "Case",
                Name = "Case",
                Category = "Cat",
                Version = "1.0.0"
            },
            EffectiveInputs = new Dictionary<string, object?>(),
            RedactedInputs = new Dictionary<string, object?>(),
            SecretInputs = new HashSet<string>(),
            EffectiveEnvironment = new Dictionary<string, string>(),
            RedactedEnvironment = new Dictionary<string, string>(),
            WorkingDir = $"..{Path.DirectorySeparatorChar}escape",
            EngineVersion = "1.0.0"
        };

        var result = await runner.RunAsync(request);
        Assert.Equal(RunStatus.Error, result.Result?.Status);
        Assert.Equal("RunnerError", result.Result?.Error?.Type);
    }

    [Fact]
    public async Task Runner_WritesRunFolderArtifactsOnValidationError()
    {
        using var temp = new TempRoot();
        var runsRoot = temp.CreateDirectory("runs");
        var caseDir = temp.CreateDirectory("case");
        File.WriteAllText(Path.Combine(caseDir, "run.ps1"), "exit 0");

        var runner = new TestCaseRunner();
        var request = new RunnerRequest
        {
            RunsRoot = runsRoot,
            TestCasePath = caseDir,
            Manifest = new TestCaseManifest
            {
                SchemaVersion = "1.5.0",
                Id = "Case",
                Name = "Case",
                Category = "Cat",
                Version = "1.0.0",
                Parameters = new List<ParameterDefinition> { new() { Name = "InputFile", Type = "file", Required = true } }
            },
            EffectiveInputs = new Dictionary<string, object?> { ["InputFile"] = "missing.txt" },
            RedactedInputs = new Dictionary<string, object?> { ["InputFile"] = "missing.txt" },
            SecretInputs = new HashSet<string>(),
            EffectiveEnvironment = new Dictionary<string, string>(),
            RedactedEnvironment = new Dictionary<string, string>(),
            EngineVersion = "1.0.0"
        };

        var result = await runner.RunAsync(request);
        Assert.Equal(RunStatus.Error, result.Result?.Status);

        var runFolder = result.RunFolder;
        Assert.True(File.Exists(Path.Combine(runFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "result.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stderr.log")));
        Assert.False(File.Exists(Path.Combine(runsRoot, "index.jsonl")));
    }

    [Fact]
    public void Runner_WritesSecretWarningEvents()
    {
        using var temp = new TempRoot();
        var runFolder = temp.CreateDirectory("run");

        RunnerEventWriter.AppendSecretWarnings(runFolder, new[] { "Token" }, "node");
        var events = File.ReadAllText(Path.Combine(runFolder, "events.jsonl"));
        Assert.Contains("EnvRef.SecretOnCommandLine", events);
        Assert.Contains("Token", events);
    }
}

internal sealed class TempRoot : IDisposable
{
    private readonly string _root;

    public TempRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
