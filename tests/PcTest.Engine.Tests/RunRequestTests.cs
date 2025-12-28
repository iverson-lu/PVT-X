using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class RunRequestTests
{
    [Fact]
    public async Task PlanRunRequestRejectsInputs()
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
        EngineTestUtilities.WriteJson(Path.Combine(caseFolder, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Case", "1.0.0"));

        var suiteFolder = Path.Combine(suitesRoot, "Suite");
        Directory.CreateDirectory(suiteFolder);
        EngineTestUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), EngineTestUtilities.SampleSuite("Suite", "1.0.0", "Case"));

        var planFolder = Path.Combine(plansRoot, "Plan");
        Directory.CreateDirectory(planFolder);
        EngineTestUtilities.WriteJson(Path.Combine(planFolder, "plan.manifest.json"), new TestPlanManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Plan",
            Name = "Plan",
            Version = "1.0.0",
            Suites = new[] { "Suite@1.0.0" }
        });

        var runner = new TestCaseRunner(new FakeProcessRunner(), new GuidRunIdGenerator());
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
                Plan = "Plan@1.0.0",
                CaseInputs = new Dictionary<string, InputValue>
                {
                    ["DurationSec"] = new InputValue(System.Text.Json.JsonDocument.Parse("1").RootElement)
                }
            },
            RunsRoot = Path.Combine(root, "Runs"),
            PowerShellPath = "pwsh",
            GroupRunIdFactory = new GuidRunIdGenerator()
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(() => engine.RunAsync(context, CancellationToken.None));
        Assert.Equal("Plan.RunRequest.Invalid", exception.Code);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
        {
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
