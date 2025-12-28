using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public sealed class PowerShellArgumentTests
{
    [Fact]
    public void Runner_PassesArraysAndBooleans()
    {
        var root = RunnerTestHelpers.CreateTempDirectory();
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(runsRoot);

        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Args",
            Name = "Args",
            Category = "Test",
            Version = "1.0.0"
        };

        var script = @"
param(
  [string[]]$Names,
  [bool]$Flag
)
Write-Output ($Names -join ',')
Write-Output ($Flag)
exit 0
";
        var (manifestPath, _) = RunnerTestHelpers.WriteTestCase(root, script, manifest);

        var runner = new RunnerService();
        var request = RunnerTestHelpers.BuildRequest(runsRoot, manifestPath, manifest,
            new Dictionary<string, object?>
            {
                ["Names"] = new List<object?> { "A", "B" },
                ["Flag"] = true
            },
            new Dictionary<string, object?>
            {
                ["Names"] = new List<object?> { "A", "B" },
                ["Flag"] = true
            },
            Array.Empty<string>());

        var result = runner.RunTestCase(request);
        var stdout = File.ReadAllText(Path.Combine(runsRoot, result.RunId, "stdout.txt"));

        Assert.Contains("A,B", stdout);
        Assert.Contains("True", stdout);
    }
}
