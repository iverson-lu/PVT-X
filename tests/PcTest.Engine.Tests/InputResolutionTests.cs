using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class InputResolutionTests
{
    [Fact]
    public async Task SuiteInputsRespectOverrideOrder()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var caseFolder = Path.Combine(casesRoot, "Case");
        Directory.CreateDirectory(caseFolder);
        EngineTestUtilities.WriteJson(Path.Combine(caseFolder, "test.manifest.json"), new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition
                {
                    Name = "DurationSec",
                    Type = "int",
                    Required = true,
                    Default = System.Text.Json.JsonDocument.Parse("1").RootElement
                }
            }
        });

        var suiteFolder = Path.Combine(suitesRoot, "Suite");
        Directory.CreateDirectory(suiteFolder);
        EngineTestUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Suite",
            Name = "Suite",
            Version = "1.0.0",
            TestCases = new[]
            {
                new TestCaseNode
                {
                    NodeId = "node-1",
                    Ref = "Case",
                    Inputs = new Dictionary<string, InputValue>
                    {
                        ["DurationSec"] = new InputValue(System.Text.Json.JsonDocument.Parse("2").RootElement)
                    }
                }
            }
        });

        var captureRunner = new CaptureProcessRunner();
        var runner = new TestCaseRunner(captureRunner, new GuidRunIdGenerator());
        var engine = new EngineRunner(runner);
        var context = new EngineRunContext
        {
            DiscoveryRoots = new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            },
            RunRequest = new RunRequest
            {
                Suite = "Suite@1.0.0",
                NodeOverrides = new Dictionary<string, NodeOverride>
                {
                    ["node-1"] = new NodeOverride
                    {
                        Inputs = new Dictionary<string, InputValue>
                        {
                            ["DurationSec"] = new InputValue(System.Text.Json.JsonDocument.Parse("3").RootElement)
                        }
                    }
                }
            },
            RunsRoot = Path.Combine(root, "Runs"),
            PowerShellPath = "pwsh",
            GroupRunIdFactory = new GuidRunIdGenerator()
        };

        await engine.RunAsync(context, CancellationToken.None);

        Assert.Contains("-DurationSec", captureRunner.Arguments);
        var index = captureRunner.Arguments.IndexOf("-DurationSec");
        Assert.Equal("3", captureRunner.Arguments[index + 1]);
    }

    private sealed class CaptureProcessRunner : IProcessRunner
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
}
