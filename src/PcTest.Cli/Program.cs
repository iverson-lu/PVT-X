using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;

namespace PcTest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pctest <discover|run-testcase|run-suite|run-plan> [options]");
            return 1;
        }

        string command = args[0];
        Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());

        switch (command)
        {
            case "discover":
                return RunDiscover(options);
            case "run-testcase":
                return await RunTestCaseAsync(options);
            case "run-suite":
                return await RunSuiteAsync(options);
            case "run-plan":
                return await RunPlanAsync(options);
            default:
                Console.Error.WriteLine($"Unknown command '{command}'.");
                return 1;
        }
    }

    private static int RunDiscover(Dictionary<string, string> options)
    {
        DiscoveryOptions discoverOptions = new()
        {
            TestCaseRoot = Require(options, "caseRoot"),
            TestSuiteRoot = Require(options, "suiteRoot"),
            TestPlanRoot = Require(options, "planRoot")
        };

        DiscoveryResult result = new DiscoveryService().Discover(discoverOptions);
        Console.WriteLine($"TestCases: {result.TestCases.Count}");
        Console.WriteLine($"TestSuites: {result.TestSuites.Count}");
        Console.WriteLine($"TestPlans: {result.TestPlans.Count}");
        if (!result.Validation.IsValid)
        {
            Console.Error.WriteLine("Discovery errors:");
            foreach (ValidationError error in result.Validation.Errors)
            {
                Console.Error.WriteLine($"- {error.Code}: {error.Message}");
            }

            return 2;
        }

        return 0;
    }

    private static async Task<int> RunTestCaseAsync(Dictionary<string, string> options)
    {
        string caseRoot = Require(options, "caseRoot");
        string runsRoot = Require(options, "runsRoot");
        string id = Require(options, "id");

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = string.Empty,
            TestPlanRoot = string.Empty
        });

        ResolvedTestCase? testCase = discovery.TestCases.FirstOrDefault(tc => tc.Identity.ToString() == id);
        if (testCase is null)
        {
            Console.Error.WriteLine($"TestCase '{id}' not found.");
            return 2;
        }

        RunRequest request = LoadRunRequest(options, new RunRequest { TestCase = id });
        RunnerService runner = new();
        EngineService engine = new(runner);
        RunConfiguration configuration = new()
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            PwshPath = options.TryGetValue("pwsh", out string? pwsh) ? pwsh : "pwsh"
        };

        CaseRunResult result = await engine.RunStandaloneTestCaseAsync(testCase, request, configuration, CancellationToken.None);
        Console.WriteLine($"RunId: {result.RunId} Status: {result.Status}");
        return 0;
    }

    private static async Task<int> RunSuiteAsync(Dictionary<string, string> options)
    {
        string caseRoot = Require(options, "caseRoot");
        string suiteRoot = Require(options, "suiteRoot");
        string runsRoot = Require(options, "runsRoot");
        string id = Require(options, "id");

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = string.Empty
        });

        ResolvedTestSuite? suite = discovery.TestSuites.FirstOrDefault(tc => tc.Identity.ToString() == id);
        if (suite is null)
        {
            Console.Error.WriteLine($"Suite '{id}' not found.");
            return 2;
        }

        RunRequest request = LoadRunRequest(options, new RunRequest { Suite = id });
        RunnerService runner = new();
        EngineService engine = new(runner);
        RunConfiguration configuration = new()
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            PwshPath = options.TryGetValue("pwsh", out string? pwsh) ? pwsh : "pwsh"
        };

        SuiteExecutionResult result = await engine.RunSuiteAsync(suite, discovery.TestCases, request, configuration, null, null, null, CancellationToken.None);
        Console.WriteLine($"RunId: {result.RunId} Status: {result.Status}");
        return 0;
    }

    private static async Task<int> RunPlanAsync(Dictionary<string, string> options)
    {
        string caseRoot = Require(options, "caseRoot");
        string suiteRoot = Require(options, "suiteRoot");
        string planRoot = Require(options, "planRoot");
        string runsRoot = Require(options, "runsRoot");
        string id = Require(options, "id");

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        ResolvedTestPlan? plan = discovery.TestPlans.FirstOrDefault(tc => tc.Identity.ToString() == id);
        if (plan is null)
        {
            Console.Error.WriteLine($"Plan '{id}' not found.");
            return 2;
        }

        RunRequest request = LoadRunRequest(options, new RunRequest { Plan = id });
        RunnerService runner = new();
        EngineService engine = new(runner);
        RunConfiguration configuration = new()
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot,
            PwshPath = options.TryGetValue("pwsh", out string? pwsh) ? pwsh : "pwsh"
        };

        PlanExecutionResult result = await engine.RunPlanAsync(plan, discovery.TestSuites, discovery.TestCases, request, configuration, CancellationToken.None);
        Console.WriteLine($"RunId: {result.RunId} Status: {result.Status}");
        return 0;
    }

    private static RunRequest LoadRunRequest(Dictionary<string, string> options, RunRequest fallback)
    {
        if (options.TryGetValue("runRequest", out string? path) && File.Exists(path))
        {
            return JsonFile.Read<RunRequest>(path);
        }

        return fallback;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = arg.Substring(2);
            if (i + 1 < args.Length)
            {
                options[key] = args[i + 1];
                i++;
            }
        }

        return options;
    }

    private static string Require(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option --{key}.");
        }

        return value;
    }
}
