using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public async Task Runner_StatusMappingFollowsExitCode()
    {
        string root = TestHelpers.CreateTempDirectory();
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(runs);

        ParameterDefinition[] parameters =
        {
            new() { Name = "ExitCode", Type = "int", Required = true }
        };

        string script = "param([int]$ExitCode)\nexit $ExitCode";
        string casePath = TestHelpers.WriteTestCase(root, script, parameters);
        TestCaseManifest manifest = JsonHelpers.ReadJsonFile<TestCaseManifest>(Path.Combine(casePath, "test.manifest.json"));

        RunnerService runner = new();

        RunnerResult passed = await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?> { ["ExitCode"] = 0 },
            new Dictionary<string, JsonElement> { ["ExitCode"] = JsonSerializer.SerializeToElement(0) }),
            CancellationToken.None);

        RunnerResult failed = await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?> { ["ExitCode"] = 1 },
            new Dictionary<string, JsonElement> { ["ExitCode"] = JsonSerializer.SerializeToElement(1) }),
            CancellationToken.None);

        RunnerResult error = await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?> { ["ExitCode"] = 5 },
            new Dictionary<string, JsonElement> { ["ExitCode"] = JsonSerializer.SerializeToElement(5) }),
            CancellationToken.None);

        Assert.Equal(RunStatus.Passed, passed.Status);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Equal(RunStatus.Error, error.Status);
    }

    [Fact]
    public async Task Runner_PassesArrayAndBoolArguments()
    {
        string root = TestHelpers.CreateTempDirectory();
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(runs);

        ParameterDefinition[] parameters =
        {
            new() { Name = "Modes", Type = "string[]", Required = true },
            new() { Name = "Flag", Type = "boolean", Required = true }
        };

        string script = "param([string[]]$Modes,[bool]$Flag)\n" +
                        "Write-Output (\"$($Modes -join ',')|$Flag\")";
        string casePath = TestHelpers.WriteTestCase(root, script, parameters);
        TestCaseManifest manifest = JsonHelpers.ReadJsonFile<TestCaseManifest>(Path.Combine(casePath, "test.manifest.json"));

        RunnerService runner = new();
        RunnerResult result = await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?> { ["Modes"] = new List<string> { "A", "B" }, ["Flag"] = true },
            new Dictionary<string, JsonElement>
            {
                ["Modes"] = JsonSerializer.SerializeToElement(new[] { "A", "B" }),
                ["Flag"] = JsonSerializer.SerializeToElement(true)
            }),
            CancellationToken.None);

        string stdout = await File.ReadAllTextAsync(Path.Combine(runs, result.RunId, "stdout.log"));
        Assert.Contains("A,B|True", stdout);
    }

    [Fact]
    public async Task Runner_RedactsSecretInputsAndWarnsOnCommandLine()
    {
        string root = TestHelpers.CreateTempDirectory();
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(runs);

        ParameterDefinition[] parameters =
        {
            new() { Name = "Secret", Type = "string", Required = true }
        };

        string script = "param([string]$Secret)\nWrite-Output $Secret";
        string casePath = TestHelpers.WriteTestCase(root, script, parameters);
        TestCaseManifest manifest = JsonHelpers.ReadJsonFile<TestCaseManifest>(Path.Combine(casePath, "test.manifest.json"));

        HashSet<string> secrets = new(StringComparer.Ordinal) { "Secret" };
        RunnerService runner = new();
        RunnerResult result = await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?> { ["Secret"] = "topsecret" },
            new Dictionary<string, JsonElement> { ["Secret"] = JsonSerializer.SerializeToElement("topsecret") },
            secrets),
            CancellationToken.None);

        string runFolder = Path.Combine(runs, result.RunId);
        string stdout = await File.ReadAllTextAsync(Path.Combine(runFolder, "stdout.log"));
        string manifestJson = await File.ReadAllTextAsync(Path.Combine(runFolder, "manifest.json"));
        string paramsJson = await File.ReadAllTextAsync(Path.Combine(runFolder, "params.json"));
        string resultJson = await File.ReadAllTextAsync(Path.Combine(runFolder, "result.json"));

        Assert.DoesNotContain("topsecret", stdout);
        Assert.Contains("***", stdout);
        Assert.DoesNotContain("topsecret", manifestJson);
        Assert.DoesNotContain("topsecret", paramsJson);
        Assert.DoesNotContain("topsecret", resultJson);

        string events = await File.ReadAllTextAsync(Path.Combine(runFolder, "events.jsonl"));
        Assert.Contains("EnvRef.SecretOnCommandLine", events);
    }

    [Fact]
    public async Task Runner_DoesNotWriteIndexJsonl()
    {
        string root = TestHelpers.CreateTempDirectory();
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(runs);

        string script = "Write-Output 'ok'";
        string casePath = TestHelpers.WriteTestCase(root, script, Array.Empty<ParameterDefinition>());
        TestCaseManifest manifest = JsonHelpers.ReadJsonFile<TestCaseManifest>(Path.Combine(casePath, "test.manifest.json"));

        RunnerService runner = new();
        await runner.RunTestCaseAsync(TestHelpers.CreateRequest(
            runs,
            casePath,
            manifest,
            new Dictionary<string, object?>(),
            new Dictionary<string, JsonElement>()),
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(runs, "index.jsonl")));
    }
}
