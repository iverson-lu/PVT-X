using PcTest.Contracts;
using PcTest.Runner;
using System.Diagnostics;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void Runner_UsesArgumentListProtocol()
    {
        using var temp = new TempFolder();
        var manifestPath = Path.Combine(temp.Path, "test.manifest.json");
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters =
            [
                new ParameterDefinition { Name = "Flag", Type = "boolean", Required = true },
                new ParameterDefinition { Name = "Items", Type = "string[]", Required = false },
                new ParameterDefinition { Name = "Count", Type = "int", Required = true }
            ]
        };

        JsonUtilities.WriteFile(manifestPath, manifest);
        var snapshot = new ManifestSnapshot
        {
            SourceManifest = manifest,
            ResolvedRef = manifestPath,
            ResolvedIdentity = new Identity("Case", "1.0.0"),
            EffectiveEnvironment = new Dictionary<string, string>(),
            EffectiveInputs = new Dictionary<string, object?>
            {
                ["Flag"] = true,
                ["Items"] = new[] { "A", "B" },
                ["Count"] = 2
            },
            ResolvedAt = DateTimeOffset.UtcNow
        };

        var fake = new FakeProcessRunner();
        var runner = new TestCaseRunner(fake);
        var context = BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>
        {
            ["Flag"] = true,
            ["Items"] = new[] { "A", "B" },
            ["Count"] = 2
        });
        runner.Run(context);

        var args = fake.LastStartInfo?.ArgumentList.ToArray();
        Assert.NotNull(args);
        Assert.Contains("-Flag", args!);
        Assert.Contains("$true", args!);
        Assert.Contains("-Items", args!);
        Assert.Contains("A", args!);
        Assert.Contains("B", args!);
        Assert.Contains("-Count", args!);
        Assert.Contains("2", args!);
    }

    [Fact]
    public void Runner_MapsExitCodesAndTimeouts()
    {
        using var temp = new TempFolder();
        var manifestPath = Path.Combine(temp.Path, "test.manifest.json");
        var manifest = BuildManifest();
        JsonUtilities.WriteFile(manifestPath, manifest);
        var snapshot = BuildSnapshot(manifestPath, manifest);

        var runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: 0) });
        var result = runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));
        Assert.Equal(RunStatus.Passed, result.Status);

        runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: 1) });
        result = runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));
        Assert.Equal(RunStatus.Failed, result.Status);

        runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: 2) });
        result = runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));
        Assert.Equal(RunStatus.Error, result.Status);
        Assert.Equal(ErrorType.ScriptError, result.Error?.Type);

        runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: null, timedOut: true) });
        result = runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));
        Assert.Equal(RunStatus.Timeout, result.Status);
        Assert.Equal(ErrorType.Timeout, result.Error?.Type);

        runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: null, aborted: true) });
        result = runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));
        Assert.Equal(RunStatus.Aborted, result.Status);
        Assert.Equal(ErrorType.Aborted, result.Error?.Type);
    }

    [Fact]
    public void Runner_RedactsSecretsInArtifacts()
    {
        using var temp = new TempFolder();
        var manifestPath = Path.Combine(temp.Path, "test.manifest.json");
        var manifest = BuildManifest();
        JsonUtilities.WriteFile(manifestPath, manifest);
        var snapshot = BuildSnapshot(manifestPath, manifest);

        var fake = new FakeProcessRunner
        {
            NextResult = BuildResult(exitCode: 0, stdout: "secret-value")
        };
        var runner = new TestCaseRunner(fake);
        var context = BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?> { ["Token"] = "secret-value" });
        context.EffectiveInputs["Token"] = "secret-value";
        context.SecretInputs.Add("Token");
        runner.Run(context);

        var runFolder = Directory.GetDirectories(temp.Path).Single();
        var manifestJson = File.ReadAllText(Path.Combine(runFolder, "manifest.json"));
        var paramsJson = File.ReadAllText(Path.Combine(runFolder, "params.json"));
        var resultJson = File.ReadAllText(Path.Combine(runFolder, "result.json"));
        var stdout = File.ReadAllText(Path.Combine(runFolder, "stdout.log"));

        Assert.Contains("***", manifestJson);
        Assert.Contains("***", paramsJson);
        Assert.Contains("***", resultJson);
        Assert.DoesNotContain("secret-value", stdout);
    }

    [Fact]
    public void Runner_RejectsEscapingWorkingDir()
    {
        using var temp = new TempFolder();
        var manifestPath = Path.Combine(temp.Path, "test.manifest.json");
        var manifest = BuildManifest();
        JsonUtilities.WriteFile(manifestPath, manifest);
        var snapshot = BuildSnapshot(manifestPath, manifest);

        var fake = new FakeProcessRunner();
        var runner = new TestCaseRunner(fake);
        var context = BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>());
        context.WorkingDir = "..";
        var result = runner.Run(context);
        Assert.Equal(RunStatus.Error, result.Status);
        Assert.Equal(ErrorType.RunnerError, result.Error?.Type);
        Assert.Equal(0, fake.RunCount);
    }

    [Fact]
    public void Runner_CreatesExpectedRunFolderLayout()
    {
        using var temp = new TempFolder();
        var manifestPath = Path.Combine(temp.Path, "test.manifest.json");
        var manifest = BuildManifest();
        JsonUtilities.WriteFile(manifestPath, manifest);
        var snapshot = BuildSnapshot(manifestPath, manifest);

        var runner = new TestCaseRunner(new FakeProcessRunner { NextResult = BuildResult(exitCode: 0) });
        runner.Run(BuildContext(temp.Path, manifestPath, manifest, snapshot, new Dictionary<string, object?>()));

        var runFolder = Directory.GetDirectories(temp.Path).Single();
        Assert.True(File.Exists(Path.Combine(runFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(runFolder, "stderr.log")));
        Assert.True(File.Exists(Path.Combine(runFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "result.json")));
    }

    private static TestCaseManifest BuildManifest() => new()
    {
        SchemaVersion = "1.5.0",
        Id = "Case",
        Name = "Case",
        Category = "Cat",
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition { Name = "Token", Type = "string", Required = false }
        ]
    };

    private static ManifestSnapshot BuildSnapshot(string manifestPath, TestCaseManifest manifest) => new()
    {
        SourceManifest = manifest,
        ResolvedRef = manifestPath,
        ResolvedIdentity = new Identity(manifest.Id, manifest.Version),
        EffectiveEnvironment = new Dictionary<string, string>(),
        EffectiveInputs = new Dictionary<string, object?> { ["Token"] = "secret-value" },
        InputTemplates = new Dictionary<string, JsonElement>(),
        ResolvedAt = DateTimeOffset.UtcNow
    };

    private static TestCaseRunContext BuildContext(string runsRoot, string manifestPath, TestCaseManifest manifest, ManifestSnapshot snapshot, Dictionary<string, object?> effectiveInputs)
    {
        return new TestCaseRunContext
        {
            RunId = "R-TEST",
            RunsRoot = runsRoot,
            TestCaseManifestPath = manifestPath,
            TestCaseManifest = manifest,
            ManifestSnapshot = snapshot,
            EffectiveInputs = effectiveInputs,
            SecretInputs = new HashSet<string>(),
            EffectiveEnvironment = new Dictionary<string, string>()
        };
    }

    private static ProcessResult BuildResult(int? exitCode, bool timedOut = false, bool aborted = false, string stdout = "", string stderr = "")
    {
        return new ProcessResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            Aborted = aborted,
            StandardOutput = stdout,
            StandardError = stderr,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow
        };
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult NextResult { get; set; } = new ProcessResult
    {
        ExitCode = 0,
        TimedOut = false,
        Aborted = false,
        StandardOutput = string.Empty,
        StandardError = string.Empty,
        StartTime = DateTimeOffset.UtcNow,
        EndTime = DateTimeOffset.UtcNow
    };

    public int RunCount { get; private set; }
    public ProcessStartInfo? LastStartInfo { get; private set; }

    public ProcessResult Run(ProcessStartInfo startInfo, TimeSpan? timeout)
    {
        RunCount++;
        LastStartInfo = startInfo;
        return NextResult;
    }
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pctest-runner-" + Guid.NewGuid().ToString("N"));
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
        }
    }
}
