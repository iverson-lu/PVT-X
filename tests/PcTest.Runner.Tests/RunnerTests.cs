using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void BuildArgumentListUsesPowerShellProtocol()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = "Cpu",
            Name = "Cpu",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Flag", Type = "boolean", Required = false },
                new ParameterDefinition { Name = "Modes", Type = "enum[]", Required = false, EnumValues = new[] { "A", "B" } },
                new ParameterDefinition { Name = "Count", Type = "int", Required = false }
            }
        };

        var args = TestCaseRunner.BuildArgumentList(manifest, new Dictionary<string, object?>
        {
            ["Flag"] = true,
            ["Modes"] = new object[] { "A", "B" },
            ["Count"] = 2
        }, "C:/scripts/run.ps1");

        Assert.Equal("C:/scripts/run.ps1", args[0]);
        Assert.Contains("-Flag", args);
        Assert.Contains("$true", args);
        Assert.Contains("-Modes", args);
        Assert.Contains("A", args);
        Assert.Contains("B", args);
    }

    [Fact]
    public async Task RunnerMapsExitCodes()
    {
        var runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(1, "", "", false, false)));
        var request = CreateRequest();
        var result = await runner.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal(RunStatus.Failed, result.Status);

        runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(2, "", "", false, false)));
        result = await runner.ExecuteAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal(RunStatus.Error, result.Status);

        runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(null, "", "", true, false)));
        result = await runner.ExecuteAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal(RunStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task WorkingDirEscapeFailsBeforeExecution()
    {
        var executor = new FakeExecutor(new PowerShellExecutionResult(0, "", "", false, false));
        var runner = new TestCaseRunner(executor);
        var request = CreateRequest();
        request = request with { WorkingDir = ".." };
        var result = await runner.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal(RunStatus.Error, result.Status);
        Assert.False(executor.WasCalled);
    }

    [Fact]
    public async Task SecretInputsAreRedactedInArtifacts()
    {
        using var temp = new TempFolder();
        var request = CreateRequest(temp.Path);
        request = request with
        {
            EffectiveInputs = new Dictionary<string, object?> { ["Token"] = "secret" },
            RedactedInputs = new Dictionary<string, object?> { ["Token"] = "***" },
            SecretInputs = new HashSet<string> { "Token" }
        };

        var runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(0, "secret", "secret", false, false)));
        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        var manifest = File.ReadAllText(Path.Combine(result.CaseRunFolder, "manifest.json"));
        var stdout = File.ReadAllText(Path.Combine(result.CaseRunFolder, "stdout.log"));
        var stderr = File.ReadAllText(Path.Combine(result.CaseRunFolder, "stderr.log"));
        Assert.DoesNotContain("secret", manifest);
        Assert.DoesNotContain("secret", stdout);
        Assert.DoesNotContain("secret", stderr);
    }

    [Fact]
    public async Task RunnerWritesExpectedCaseRunLayout()
    {
        using var temp = new TempFolder();
        var request = CreateRequest(temp.Path);
        var runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(0, "ok", "", false, false)));
        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "stderr.log")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(result.CaseRunFolder, "result.json")));
    }

    [Fact]
    public void RunnerDoesNotWriteIndexJsonl()
    {
        using var temp = new TempFolder();
        var runner = new TestCaseRunner(new FakeExecutor(new PowerShellExecutionResult(0, "", "", false, false)));
        var request = CreateRequest(temp.Path);
        runner.ExecuteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(File.Exists(Path.Combine(temp.Path, "index.jsonl")));
    }

    private static TestCaseRunRequest CreateRequest(string? runsRoot = null)
    {
        var root = runsRoot ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pctest-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var caseRoot = System.IO.Path.Combine(root, "Case");
        Directory.CreateDirectory(caseRoot);
        File.WriteAllText(System.IO.Path.Combine(caseRoot, "run.ps1"), "Write-Output 'ok'; exit 0");

        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = "Cpu",
            Name = "Cpu",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Flag", Type = "boolean", Required = false }
            }
        };

        return new TestCaseRunRequest
        {
            RunsRoot = root,
            CaseRoot = caseRoot,
            ResolvedRef = caseRoot,
            Identity = new Identity("Cpu", "1.0.0"),
            Manifest = manifest,
            EffectiveInputs = new Dictionary<string, object?> { ["Flag"] = true },
            RedactedInputs = new Dictionary<string, object?> { ["Flag"] = true },
            EffectiveEnvironment = new Dictionary<string, string>(),
            SecretInputs = new HashSet<string>(),
            TimeoutSec = 1
        };
    }

    private sealed class FakeExecutor : IPowerShellExecutor
    {
        private readonly PowerShellExecutionResult _result;

        public FakeExecutor(PowerShellExecutionResult result)
        {
            _result = result;
        }

        public bool WasCalled { get; private set; }

        public Task<PowerShellExecutionResult> ExecuteAsync(PowerShellExecutionRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pctest-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
