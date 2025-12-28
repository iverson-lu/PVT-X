using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine.Tests;

internal static class EngineTestUtilities
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pctest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WriteJson<T>(string path, T value)
    {
        JsonParsing.WriteDeterministic(path, value);
    }

    public static TestCaseManifest SampleTestCase(string id, string version)
    {
        return new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = id,
            Name = id,
            Category = "Cat",
            Version = version,
            Parameters = new[]
            {
                new ParameterDefinition
                {
                    Name = "DurationSec",
                    Type = "int",
                    Required = true,
                    Default = JsonDocument.Parse("1").RootElement
                },
                new ParameterDefinition
                {
                    Name = "Mode",
                    Type = "enum",
                    Required = false,
                    EnumValues = new[] { "A", "B" },
                    Default = JsonDocument.Parse("\"A\"").RootElement
                }
            }
        };
    }

    public static TestSuiteManifest SampleSuite(string id, string version, string refValue)
    {
        return new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = id,
            Name = id,
            Version = version,
            TestCases = new[]
            {
                new TestCaseNode
                {
                    NodeId = "node-1",
                    Ref = refValue,
                    Inputs = new Dictionary<string, InputValue>
                    {
                        ["DurationSec"] = new InputValue(JsonDocument.Parse("1").RootElement)
                    }
                }
            }
        };
    }
}
