using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public async Task UsesPowerShellArgumentListProtocol()
    {
        var capture = new CaptureRunner();
        var runner = new TestCaseRunner(capture, new FixedRunIdGenerator("run-1"));
        var context = CreateContext(new Dictionary<string, object>
        {
            ["Flag"] = true,
            ["Modes"] = new[] { "A", "B" }
        }, new Dictionary<string, ParameterDefinition>
        {
            ["Flag"] = new ParameterDefinition { Name = "Flag", Type = "boolean", Required = false },
            ["Modes"] = new ParameterDefinition { Name = "Modes", Type = "string[]", Required = false }
        });

        await runner.RunAsync(context, CancellationToken.None);

        Assert.Contains("-Flag", capture.Arguments);
        var flagIndex = capture.Arguments.IndexOf("-Flag");
        Assert.Equal("$true", capture.Arguments[flagIndex + 1]);

        var modesIndex = capture.Arguments.IndexOf("-Modes");
        Assert.Equal("A", capture.Arguments[modesIndex + 1]);
        Assert.Equal("B", capture.Arguments[modesIndex + 2]);
    }

    [Fact]
    public async Task MapsProcessExitCodesToStatuses()
    {
        var runner = new TestCaseRunner(new StubRunner(0, false, false), new FixedRunIdGenerator("run-1"));
        var result = await runner.RunAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(ResultStatus.Passed, result.Status);

        runner = new TestCaseRunner(new StubRunner(1, false, false), new FixedRunIdGenerator("run-2"));
        result = await runner.RunAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(ResultStatus.Failed, result.Status);

        runner = new TestCaseRunner(new StubRunner(2, false, false), new FixedRunIdGenerator("run-3"));
        result = await runner.RunAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(ResultStatus.Error, result.Status);

        runner = new TestCaseRunner(new StubRunner(0, true, false), new FixedRunIdGenerator("run-4"));
        result = await runner.RunAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(ResultStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task RejectsWorkingDirectoryEscape()
    {
        var capture = new CaptureRunner();
        var runner = new TestCaseRunner(capture, new FixedRunIdGenerator("run-1"));
        var context = CreateContext();
        context = context with { WorkingDir = ".." };

        var result = await runner.RunAsync(context, CancellationToken.None);
        Assert.Equal(ResultStatus.Error, result.Status);
        Assert.Empty(capture.Arguments);
    }

    [Fact]
    public async Task WritesRunFolderLayout()
    {
        var root = CreateTempDirectory();
        var runner = new TestCaseRunner(new StubRunner(0, false, false), new FixedRunIdGenerator("run-layout"));
        var context = CreateContext();
        context = context with { RunsRoot = root };

        await runner.RunAsync(context, CancellationToken.None);

        var runFolder = Path.Combine(root, "run-layout");
        Assert.True(File.Exists(Path.Combine(runFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stdout.txt")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stderr.txt")));
        Assert.True(File.Exists(Path.Combine(runFolder, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(runFolder, "result.json")));
    }

    [Fact]
    public async Task RedactsSecretInputsInArtifacts()
    {
        var root = CreateTempDirectory();
        var runner = new TestCaseRunner(new StubRunner(0, false, false), new FixedRunIdGenerator("run-secret"));
        var context = CreateContext(new Dictionary<string, object> { ["Token"] = "secret" }, new Dictionary<string, ParameterDefinition>
        {
            ["Token"] = new ParameterDefinition { Name = "Token", Type = "string", Required = false }
        });
        context = context with
        {
            RunsRoot = root,
            SecretInputValues = new Dictionary<string, IReadOnlyList<string>> { ["Token"] = new[] { "secret" } }
        };

        await runner.RunAsync(context, CancellationToken.None);

        var manifestJson = File.ReadAllText(Path.Combine(root, "run-secret", "manifest.json"));
        Assert.Contains("\"Token\":\"***\"", manifestJson);

        var eventsJson = File.ReadAllText(Path.Combine(root, "run-secret", "events.jsonl"));
        Assert.Contains("EnvRef.SecretOnCommandLine", eventsJson);
    }

    private static CaseRunContext CreateContext(Dictionary<string, object>? inputs = null, Dictionary<string, ParameterDefinition>? definitions = null)
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(root);
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = definitions?.Values.ToArray() ?? Array.Empty<ParameterDefinition>()
        };

        return new CaseRunContext
        {
            RunsRoot = root,
            PowerShellPath = "pwsh",
            ScriptPath = Path.Combine(root, "run.ps1"),
            TestCaseManifest = manifest,
            ResolvedRef = Path.Combine(root, "test.manifest.json"),
            EffectiveInputs = inputs ?? new Dictionary<string, object>(),
            EffectiveEnvironment = new Dictionary<string, string>(),
            SecretInputValues = new Dictionary<string, IReadOnlyList<string>>(),
            SecretEnvironmentKeys = new HashSet<string>(StringComparer.Ordinal),
            ParameterDefinitions = definitions ?? new Dictionary<string, ParameterDefinition>(),
            WorkingDir = null
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pctest_runner_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CaptureRunner : IProcessRunner
    {
        public List<string> Arguments { get; } = new();

        public Task<ProcessRunResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            Arguments.AddRange(startInfo.ArgumentList);
            return Task.FromResult(new ProcessRunResult
            {
                ExitCode = 0,
                TimedOut = false,
                Aborted = false,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            });
        }
    }

    private sealed class StubRunner : IProcessRunner
    {
        private readonly int _exitCode;
        private readonly bool _timedOut;
        private readonly bool _aborted;

        public StubRunner(int exitCode, bool timedOut, bool aborted)
        {
            _exitCode = exitCode;
            _timedOut = timedOut;
            _aborted = aborted;
        }

        public Task<ProcessRunResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessRunResult
            {
                ExitCode = _exitCode,
                TimedOut = _timedOut,
                Aborted = _aborted,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                ErrorMessage = _timedOut ? "Timeout" : null
            });
        }
    }

    private sealed class FixedRunIdGenerator : IRunIdGenerator
    {
        private readonly string _id;

        public FixedRunIdGenerator(string id)
        {
            _id = id;
        }

        public string NewRunId() => _id;
    }
}
