using PcTest.Contracts;
using PcTest.Engine;

namespace PcTest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pctest <discover|run> ...");
            return 1;
        }

        string command = args[0];
        string[] remaining = args.Skip(1).ToArray();
        return command switch
        {
            "discover" => RunDiscover(remaining),
            "run" => await RunExecuteAsync(remaining).ConfigureAwait(false),
            _ => PrintError("Unknown command.")
        };
    }

    private static int RunDiscover(string[] args)
    {
        Paths paths = Paths.FromArgs(args);
        EngineService engine = new();
        ValidationResult<DiscoveryResult> result = engine.Discover(paths.TestCaseRoot, paths.SuiteRoot, paths.PlanRoot);
        if (!result.IsSuccess)
        {
            foreach (ValidationError error in result.Errors)
            {
                Console.Error.WriteLine($"{error.Code}: {error.Message}");
            }

            return 1;
        }

        Console.WriteLine($"TestCases: {result.Value!.TestCases.Count}");
        Console.WriteLine($"Suites: {result.Value!.Suites.Count}");
        Console.WriteLine($"Plans: {result.Value!.Plans.Count}");
        return 0;
    }

    private static async Task<int> RunExecuteAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return PrintError("Usage: pctest run <testCase|suite|plan> <id@version> [--request path]");
        }

        string kind = args[0];
        string id = args[1];
        if (kind is not ("testCase" or "suite" or "plan"))
        {
            return PrintError("Run kind must be testCase, suite, or plan.");
        }
        string[] remaining = args.Skip(2).ToArray();
        Paths paths = Paths.FromArgs(remaining);
        string? requestPath = GetOptionValue(remaining, "--request");

        RunRequest loadedRequest = requestPath is null ? new RunRequest() : JsonHelpers.ReadJsonFile<RunRequest>(requestPath);
        RunRequest runRequest = new()
        {
            Suite = kind == "suite" ? id : null,
            Plan = kind == "plan" ? id : null,
            TestCase = kind == "testCase" ? id : null,
            NodeOverrides = loadedRequest.NodeOverrides,
            CaseInputs = loadedRequest.CaseInputs,
            EnvironmentOverrides = loadedRequest.EnvironmentOverrides
        };

        EngineService engine = new();
        ValidationResult<DiscoveryResult> discovery = engine.Discover(paths.TestCaseRoot, paths.SuiteRoot, paths.PlanRoot);
        if (!discovery.IsSuccess)
        {
            foreach (ValidationError error in discovery.Errors)
            {
                Console.Error.WriteLine($"{error.Code}: {error.Message}");
            }

            return 1;
        }

        ValidationResult<string> runResult = await engine.RunAsync(discovery.Value!, runRequest, paths.RunsRoot, CancellationToken.None).ConfigureAwait(false);
        if (!runResult.IsSuccess)
        {
            foreach (ValidationError error in runResult.Errors)
            {
                Console.Error.WriteLine($"{error.Code}: {error.Message}");
            }

            return 1;
        }

        Console.WriteLine($"RunId: {runResult.Value}");
        return 0;
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int PrintError(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed record Paths(string TestCaseRoot, string SuiteRoot, string PlanRoot, string RunsRoot)
    {
        public static Paths FromArgs(string[] args)
        {
            string cwd = Directory.GetCurrentDirectory();
            string testCaseRoot = GetOptionValue(args, "--cases") ?? Path.Combine(cwd, "assets", "TestCases");
            string suiteRoot = GetOptionValue(args, "--suites") ?? Path.Combine(cwd, "assets", "TestSuites");
            string planRoot = GetOptionValue(args, "--plans") ?? Path.Combine(cwd, "assets", "TestPlans");
            string runsRoot = GetOptionValue(args, "--runs") ?? Path.Combine(cwd, "Runs");
            return new Paths(testCaseRoot, suiteRoot, planRoot, runsRoot);
        }
    }
}
