using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Runner.Tests;

public static class TestHelpers
{
    public static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "PctestRunner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string WriteTestCase(string root, string scriptContent, ParameterDefinition[]? parameters = null)
    {
        string folder = Path.Combine(root, "Case");
        Directory.CreateDirectory(folder);
        TestCaseManifest manifest = new()
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = parameters
        };
        JsonHelpers.WriteJsonFile(Path.Combine(folder, "test.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(folder, "run.ps1"), scriptContent, new System.Text.UTF8Encoding(false));
        return folder;
    }

    public static RunnerRequest CreateRequest(string runsRoot, string casePath, TestCaseManifest manifest, Dictionary<string, object?> inputs, Dictionary<string, JsonElement> inputsJson, HashSet<string>? secrets = null)
    {
        return new RunnerRequest
        {
            RunsRoot = runsRoot,
            TestCasePath = casePath,
            Manifest = manifest,
            ResolvedRef = Path.Combine(casePath, "test.manifest.json"),
            Identity = new Identity(manifest.Id, manifest.Version),
            EffectiveEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            EffectiveInputs = inputs,
            EffectiveInputsJson = inputsJson,
            InputTemplates = inputsJson,
            SecretInputs = secrets ?? new HashSet<string>(StringComparer.Ordinal)
        };
    }
}
