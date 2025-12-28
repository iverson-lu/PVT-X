using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;

static class Program
{
    private static readonly string DefaultTestCaseRoot = Path.Combine("assets", "TestCases");
    private static readonly string DefaultSuiteRoot = Path.Combine("assets", "TestSuites");
    private static readonly string DefaultPlanRoot = Path.Combine("assets", "TestPlans");
    private static readonly string DefaultRunsRoot = Path.Combine("Runs");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "discover" => Discover(args.Skip(1).ToArray()),
            "run" => await RunAsync(args.Skip(1).ToArray()),
            _ => UsageError()
        };
    }

    private static int Discover(string[] args)
    {
        var (testCaseRoot, suiteRoot, planRoot) = ParseRoots(args);
        var discovery = DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = testCaseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        Console.WriteLine($"TestCases: {discovery.TestCases.Count}");
        Console.WriteLine($"TestSuites: {discovery.Suites.Count}");
        Console.WriteLine($"TestPlans: {discovery.Plans.Count}");
        return 0;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return UsageError();
        }

        var target = args[0].ToLowerInvariant();
        var identity = args[1];
        var (testCaseRoot, suiteRoot, planRoot) = ParseRoots(args.Skip(2).ToArray());
        var runsRoot = ParseRunsRoot(args.Skip(2).ToArray());

        var discovery = DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = testCaseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        var runner = new PwshRunner(new ProcessRunner());
        var engine = new EngineService(runner, new EngineOptions { RunsRoot = runsRoot });
        var request = target switch
        {
            "testcase" => new RunRequest { TestCase = identity },
            "suite" => new RunRequest { Suite = identity },
            "plan" => new RunRequest { Plan = identity },
            _ => throw new ArgumentException("Unknown run target.")
        };

        await engine.ExecuteAsync(discovery, request, CancellationToken.None);
        Console.WriteLine("Run completed.");
        return 0;
    }

    private static (string testCaseRoot, string suiteRoot, string planRoot) ParseRoots(string[] args)
    {
        var testCaseRoot = DefaultTestCaseRoot;
        var suiteRoot = DefaultSuiteRoot;
        var planRoot = DefaultPlanRoot;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--testCaseRoot":
                    testCaseRoot = args[++i];
                    break;
                case "--suiteRoot":
                    suiteRoot = args[++i];
                    break;
                case "--planRoot":
                    planRoot = args[++i];
                    break;
            }
        }

        return (testCaseRoot, suiteRoot, planRoot);
    }

    private static string ParseRunsRoot(string[] args)
    {
        var runsRoot = DefaultRunsRoot;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--runsRoot")
            {
                runsRoot = args[++i];
            }
        }

        return runsRoot;
    }

    private static int UsageError()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PcTest.Cli");
        Console.WriteLine("  discover [--testCaseRoot PATH] [--suiteRoot PATH] [--planRoot PATH]");
        Console.WriteLine("  run testcase <id@version> [--runsRoot PATH]");
        Console.WriteLine("  run suite <id@version> [--runsRoot PATH]");
        Console.WriteLine("  run plan <id@version> [--runsRoot PATH]");
    }
}
