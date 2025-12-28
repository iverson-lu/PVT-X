using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner.Tests;

public static class RunnerTestHelpers
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pctest-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static (string manifestPath, TestCaseManifest manifest) WriteTestCase(string root, string scriptBody, TestCaseManifest manifest)
    {
        var folder = Path.Combine(root, manifest.Id);
        Directory.CreateDirectory(folder);
        var manifestPath = Path.Combine(folder, "test.manifest.json");
        JsonUtils.WriteJsonFile(manifestPath, manifest);
        File.WriteAllText(Path.Combine(folder, "run.ps1"), scriptBody);
        return (manifestPath, manifest);
    }

    public static TestCaseExecutionRequest BuildRequest(string runsRoot, string manifestPath, TestCaseManifest manifest, Dictionary<string, object?> inputs, Dictionary<string, object?> redactedInputs, IEnumerable<string> secrets)
    {
        using var doc = JsonUtils.ReadJsonDocument(manifestPath);
        return new TestCaseExecutionRequest
        {
            TestCasePath = manifestPath,
            TestCase = manifest,
            SourceManifest = doc.RootElement.Clone(),
            ResolvedRef = Path.GetDirectoryName(manifestPath) ?? string.Empty,
            Identity = new Identity(manifest.Id, manifest.Version),
            RunId = RunIdGenerator.NewRunId(),
            RunsRoot = runsRoot,
            EffectiveInputs = inputs,
            RedactedInputs = redactedInputs,
            SecretInputs = secrets.ToArray(),
            EffectiveEnvironment = new Dictionary<string, string>(),
            WorkingDir = null,
            EngineVersion = "test"
        };
    }
}
