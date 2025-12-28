using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine.Tests;

public static class TestHelpers
{
    public static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Pctest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WriteTestCase(string root, string name, string id, string version)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        TestCaseManifest manifest = new()
        {
            SchemaVersion = "1.5.0",
            Id = id,
            Name = id,
            Category = "Cat",
            Version = version,
            Parameters = new[]
            {
                new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonSerializer.SerializeToElement(1) }
            }
        };
        JsonHelpers.WriteJsonFile(Path.Combine(folder, "test.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(folder, "run.ps1"), "param([int]$DurationSec)\nexit 0", new System.Text.UTF8Encoding(false));
    }

    public static void WriteSuite(string root, string name, SuiteManifest manifest)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        JsonHelpers.WriteJsonFile(Path.Combine(folder, "suite.manifest.json"), manifest);
    }

    public static void WritePlan(string root, string name, PlanManifest manifest)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        JsonHelpers.WriteJsonFile(Path.Combine(folder, "plan.manifest.json"), manifest);
    }
}
