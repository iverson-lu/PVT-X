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
            Console.Error.WriteLine("Usage: pc-test <discover|run>");
            return 1;
        }

        string command = args[0];
        var options = ParseOptions(args.Skip(1).ToArray());
        string baseDir = AppContext.BaseDirectory;

        string testCaseRoot = options.GetValueOrDefault("--testCaseRoot") ?? Path.Combine(baseDir, "assets", "TestCases");
        string testSuiteRoot = options.GetValueOrDefault("--testSuiteRoot") ?? Path.Combine(baseDir, "assets", "TestSuites");
        string testPlanRoot = options.GetValueOrDefault("--testPlanRoot") ?? Path.Combine(baseDir, "assets", "TestPlans");
        string runsRoot = options.GetValueOrDefault("--runsRoot") ?? Path.Combine(baseDir, "Runs");

        var roots = new ResolvedRoots(testCaseRoot, testSuiteRoot, testPlanRoot, runsRoot);
        var engine = new EngineService(new DiscoveryService(), new RunnerService(new PowerShellProcessRunner()));

        if (string.Equals(command, "discover", StringComparison.OrdinalIgnoreCase))
        {
            DiscoveryResult result = engine.Discover(roots);
            Console.WriteLine($"TestCases: {result.TestCases.Count}");
            Console.WriteLine($"TestSuites: {result.TestSuites.Count}");
            Console.WriteLine($"TestPlans: {result.TestPlans.Count}");
            return 0;
        }

        if (string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: pc-test run <testCase|suite|plan> --id <id@version> [--request <path>]");
                return 1;
            }

            string runTarget = args[1];
            string? id = options.GetValueOrDefault("--id");
            if (string.IsNullOrWhiteSpace(id))
            {
                Console.Error.WriteLine("--id is required.");
                return 1;
            }

            RunRequest request;
            if (options.TryGetValue("--request", out var requestPath))
            {
                request = JsonUtilities.ReadJsonFile<RunRequest>(requestPath);
            }
            else
            {
                request = runTarget switch
                {
                    "testCase" => new RunRequest { TestCase = id },
                    "suite" => new RunRequest { Suite = id },
                    "plan" => new RunRequest { Plan = id },
                    _ => throw new InvalidOperationException("Unknown run target.")
                };
            }

            await engine.RunAsync(request, roots, CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        Console.Error.WriteLine($"Unknown command '{command}'.");
        return 1;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}.");
                }

                options[arg] = args[i + 1];
                i++;
            }
        }

        return options;
    }
}
