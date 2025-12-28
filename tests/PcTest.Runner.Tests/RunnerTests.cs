using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void ArgumentSerializer_Formats_Bools_And_Arrays()
    {
        IReadOnlyList<string> args = ArgumentSerializer.Serialize("Flag", "boolean", true);
        Assert.Equal(new[] { "-Flag", "$true" }, args);

        IReadOnlyList<string> arrayArgs = ArgumentSerializer.Serialize("Modes", "string[]", new[] { "A", "B" });
        Assert.Equal(new[] { "-Modes", "A", "B" }, arrayArgs);
    }

    [Fact]
    public async Task Runner_Maps_Exit_Codes()
    {
        using TempDirectory temp = new();
        RunnerService runner = new(new FakeProcessRunner(0));
        CaseRunRequest request = CreateRequest(temp.Path, new Dictionary<string, object>());
        CaseRunResult result = await runner.RunAsync(request, CancellationToken.None);
        Assert.Equal("Passed", result.Status);

        RunnerService runnerFail = new(new FakeProcessRunner(1));
        result = await runnerFail.RunAsync(request, CancellationToken.None);
        Assert.Equal("Failed", result.Status);

        RunnerService runnerError = new(new FakeProcessRunner(5));
        result = await runnerError.RunAsync(request, CancellationToken.None);
        Assert.Equal("Error", result.Status);
        Assert.Equal("ScriptError", result.ErrorType);
    }

    [Fact]
    public async Task Runner_Redacts_Secrets_In_Params_And_Result()
    {
        using TempDirectory temp = new();
        RunnerService runner = new(new FakeProcessRunner(0));
        Dictionary<string, object> inputs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Secret"] = "super"
        };
        CaseRunRequest request = CreateRequest(temp.Path, inputs, secretKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Secret" });
        CaseRunResult result = await runner.RunAsync(request, CancellationToken.None);

        string runFolder = Path.Combine(temp.Path, result.RunId);
        string paramsJson = File.ReadAllText(Path.Combine(runFolder, "params.json"));
        Assert.Contains("***", paramsJson);

        string resultJson = File.ReadAllText(Path.Combine(runFolder, "result.json"));
        Assert.Contains("***", resultJson);

        string eventsPath = Path.Combine(runFolder, "events.jsonl");
        Assert.True(File.Exists(eventsPath));
        string eventsContent = File.ReadAllText(eventsPath);
        Assert.Contains("EnvRef.SecretOnCommandLine", eventsContent);
    }

    [Fact]
    public async Task Runner_Rejects_WorkingDir_Escape()
    {
        using TempDirectory temp = new();
        RunnerService runner = new(new FakeProcessRunner(0));
        CaseRunRequest request = CreateRequest(temp.Path, new Dictionary<string, object>(), workingDir: "..\\outside");
        CaseRunResult result = await runner.RunAsync(request, CancellationToken.None);
        Assert.Equal("Error", result.Status);
        Assert.Equal("RunnerError", result.ErrorType);
    }

    [Fact]
    public async Task Runner_Does_Not_Write_Index()
    {
        using TempDirectory temp = new();
        RunnerService runner = new(new FakeProcessRunner(0));
        CaseRunRequest request = CreateRequest(temp.Path, new Dictionary<string, object>());
        _ = await runner.RunAsync(request, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(temp.Path, "index.jsonl")));
    }

    private static CaseRunRequest CreateRequest(string runsRoot, Dictionary<string, object> inputs, HashSet<string>? secretKeys = null, string? workingDir = null)
    {
        using JsonDocument manifest = JsonDocument.Parse("{\"schemaVersion\":\"1.5.0\",\"id\":\"Case\",\"name\":\"Case\",\"category\":\"Demo\",\"version\":\"1.0.0\"}");
        return new CaseRunRequest
        {
            RunsRoot = runsRoot,
            ManifestPath = Path.Combine(runsRoot, "manifest.json"),
            ScriptPath = Path.Combine(runsRoot, "run.ps1"),
            TestId = "Case",
            TestVersion = "1.0.0",
            EffectiveInputs = inputs,
            RedactedInputs = secretKeys is null ? inputs : inputs.ToDictionary(k => k.Key, _ => (object)"***", StringComparer.OrdinalIgnoreCase),
            SecretKeys = secretKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            EffectiveEnvironment = new Dictionary<string, string>(),
            SourceManifest = manifest.RootElement.Clone(),
            Parameters = inputs.Keys.Select(name => new ParameterDefinitionSnapshot { Name = name, Type = "string" }).ToList(),
            WorkingDir = workingDir
        };
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly int _exitCode;
    private readonly bool _timeout;

    public FakeProcessRunner(int exitCode, bool timeout = false)
    {
        _exitCode = exitCode;
        _timeout = timeout;
    }

    public Task<ProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessResult
        {
            ExitCode = _timeout ? null : _exitCode,
            TimedOut = _timeout,
            Aborted = false,
            Stdout = "secret-output",
            Stderr = string.Empty
        });
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pctest-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}
