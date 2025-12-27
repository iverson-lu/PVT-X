using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public class RunFolderLayoutTests
{
    [Fact]
    public void Runner_WritesRequiredArtifacts_OnValidationError()
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
            EffectiveInputs = new Dictionary<string, object?>(),
            InputTemplates = new Dictionary<string, object?>(),
            EffectiveEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SecretInputs = new HashSet<string>(),
            WorkingDir = ".."
        };

        var result = runner.Run(request);
        Assert.Equal("Error", result.Status);
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "stderr.log")));
        Assert.True(File.Exists(Path.Combine(result.RunFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(temp, "index.jsonl")));
    }
}
