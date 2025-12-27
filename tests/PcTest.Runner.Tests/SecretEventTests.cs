using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public class SecretEventTests
{
    [Fact]
    public void Runner_EmitsSecretOnCommandLineWarning()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pctest-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var runner = new PowerShellRunner();
        var request = new RunCaseRequest
        {
            RunsRoot = temp,
            Manifest = new TestCaseManifest { Id = "Case", Name = "Case", Category = "Cat", Version = "1.0.0" },
            ManifestPath = System.IO.Path.Combine(temp, "test.manifest.json"),
            ResolvedRef = System.IO.Path.Combine(temp, "run.ps1"),
            EffectiveInputs = new Dictionary<string, object?> { ["Secret"] = "value" },
            InputTemplates = new Dictionary<string, object?>(),
            EffectiveEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SecretInputs = new HashSet<string> { "Secret" },
            WorkingDir = "work"
        };

        var result = runner.Run(request);
        var eventsPath = Path.Combine(result.RunFolder, "events.jsonl");
        Assert.True(File.Exists(eventsPath));
        var content = File.ReadAllText(eventsPath);
        Assert.Contains("EnvRef.SecretOnCommandLine", content);
    }
}
