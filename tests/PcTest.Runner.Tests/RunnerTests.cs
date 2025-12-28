using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void PowerShellArgumentProtocolUsesRepeatedArrayAndBoolLiteral()
    {
        var request = TestRequestFactory.CreateRequest(new Dictionary<string, object?>
        {
            ["DurationSec"] = 5,
            ["Modes"] = new List<object?> { "A", "B" },
            ["Enabled"] = true
        });

        var (args, warnings) = PwshRunner.BuildArgumentList(request);
        Assert.Empty(warnings);
        Assert.Equal(new[]
        {
            "-DurationSec", "5",
            "-Modes", "A", "B",
            "-Enabled", "$true"
        }, args);
    }

    [Theory]
    [InlineData(0, RunStatus.Passed)]
    [InlineData(1, RunStatus.Failed)]
    [InlineData(2, RunStatus.Error)]
    public void StatusMappingRespectsExitCode(int exitCode, RunStatus expected)
    {
        var result = PwshRunner.MapStatus(new ProcessResult(exitCode, string.Empty, string.Empty, false, false, null));
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void StatusMappingTimeoutWins()
    {
        var result = PwshRunner.MapStatus(new ProcessResult(null, string.Empty, string.Empty, true, false, null));
        Assert.Equal(RunStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task SecretRedactionIsAppliedToArtifacts()
    {
        using var temp = new TempFolder();
        var runner = new PwshRunner(new FakeProcessRunner("Token=SECRET"));
        var request = TestRequestFactory.CreateRequest(new Dictionary<string, object?>
        {
            ["Token"] = "SECRET"
        }, secretInputs: new[] { "Token" }, runsRoot: temp.Root);

        var result = await runner.RunCaseAsync(request, CancellationToken.None);
        var runFolder = Path.Combine(temp.Root, result.RunId);

        var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(runFolder, "manifest.json"))).RootElement;
        var paramsJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(runFolder, "params.json"))).RootElement;
        var resultJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(runFolder, "result.json"))).RootElement;
        var stdout = File.ReadAllText(Path.Combine(runFolder, "stdout.log"));

        Assert.Equal("***", paramsJson.GetProperty("Token").GetString());
        Assert.Equal("***", resultJson.GetProperty("effectiveInputs").GetProperty("Token").GetString());
        Assert.Equal("***", manifest.GetProperty("effectiveInputs").GetProperty("Token").GetString());
        Assert.DoesNotContain("SECRET", stdout);
    }

    [Fact]
    public async Task WorkingDirEscapeFailsBeforeProcessStart()
    {
        using var temp = new TempFolder();
        var fakeRunner = new FakeProcessRunner();
        var runner = new PwshRunner(fakeRunner);
        var request = TestRequestFactory.CreateRequest(
            new Dictionary<string, object?>(),
            runsRoot: temp.Root,
            workingDir: "../escape");

        var result = await runner.RunCaseAsync(request, CancellationToken.None);
        Assert.Equal(RunStatus.Error, result.Status);
        Assert.False(fakeRunner.WasInvoked);
    }

    [Fact]
    public async Task RunFolderLayoutAndOwnershipAreEnforced()
    {
        using var temp = new TempFolder();
        var testCaseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");

        Directory.CreateDirectory(Path.Combine(testCaseRoot, "Case"));
        File.WriteAllText(Path.Combine(testCaseRoot, "Case", "test.manifest.json"), """
        {
          "schemaVersion": "1.5.0",
          "id": "Case",
          "name": "Case",
          "category": "Cat",
          "version": "1.0.0"
        }
        """);
        File.WriteAllText(Path.Combine(testCaseRoot, "Case", "run.ps1"), "Write-Output 'ok'");

        Directory.CreateDirectory(Path.Combine(suiteRoot, "Suite"));
        File.WriteAllText(Path.Combine(suiteRoot, "Suite", "suite.manifest.json"), """
        {
          "schemaVersion": "1.5.0",
          "id": "Suite",
          "name": "Suite",
          "version": "1.0.0",
          "testCases": [
            { "nodeId": "node", "ref": "Case" }
          ]
        }
        """);

        var discovery = DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = testCaseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        var runsRoot = temp.CreateSubfolder("runs");
        var engine = new EngineService(new PwshRunner(new FakeProcessRunner()), new EngineOptions { RunsRoot = runsRoot });

        var caseResult = (CaseRunResult)await engine.ExecuteAsync(discovery, new RunRequest { TestCase = "Case@1.0.0" }, CancellationToken.None);
        var caseFolder = Path.Combine(runsRoot, caseResult.RunId);

        Assert.True(File.Exists(Path.Combine(caseFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "stderr.log")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "controls.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "children.jsonl")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "runRequest.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "index.jsonl")));
        Assert.True(File.Exists(Path.Combine(runsRoot, "index.jsonl")));
        Assert.Empty(Directory.GetDirectories(runsRoot, "G-*"));

        var suiteResult = (GroupRunResult)await engine.ExecuteAsync(discovery, new RunRequest { Suite = "Suite@1.0.0" }, CancellationToken.None);
        var groupFolder = Path.Combine(runsRoot, suiteResult.RunId);

        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "controls.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "environment.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(groupFolder, "params.json")));
    }
}

internal static class TestRequestFactory
{
    public static CaseRunRequest CreateRequest(
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyCollection<string>? secretInputs = null,
        string? runsRoot = null,
        string? workingDir = null)
    {
        var root = runsRoot ?? Path.Combine(Path.GetTempPath(), "PcTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manifest = JsonUtils.Parse("{\"schemaVersion\":\"1.5.0\",\"id\":\"Case\",\"name\":\"Case\",\"category\":\"Cat\",\"version\":\"1.0.0\"}");
        return new CaseRunRequest(
            root,
            root,
            Path.Combine(root, "test.manifest.json"),
            root,
            new Identity("Case", "1.0.0"),
            manifest,
            new Dictionary<string, string>(),
            inputs,
            inputs.ToDictionary(kv => kv.Key, kv => kv.Value),
            new Dictionary<string, string>(),
            new Dictionary<string, JsonElement>(),
            secretInputs ?? Array.Empty<string>(),
            workingDir,
            null,
            null,
            null,
            null,
            "Test"
        );
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ProcessResult _result;

    public FakeProcessRunner(string? output = null)
    {
        _result = new ProcessResult(0, output ?? string.Empty, output ?? string.Empty, false, false, null);
    }

    public bool WasInvoked { get; private set; }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, int? timeoutSec, CancellationToken cancellationToken)
    {
        WasInvoked = true;
        return Task.FromResult(_result);
    }
}

internal sealed class TempFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PcTest", Guid.NewGuid().ToString("N"));

    public TempFolder()
    {
        Directory.CreateDirectory(Root);
    }

    public string CreateSubfolder(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
