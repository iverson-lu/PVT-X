using System.Text.Json;
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
            Console.Error.WriteLine("Usage: pctest <discover|run> ...");
            return 1;
        }

        var command = args[0];
        var options = ParseOptions(args.Skip(1));

        try
        {
            switch (command)
            {
                case "discover":
                    return RunDiscover(options);
                case "run":
                    return await RunExecutionAsync(options).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine("Unknown command.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunDiscover(Dictionary<string, List<string>> options)
    {
        var roots = new DiscoveryRoots
        {
            ResolvedTestCaseRoot = RequireSingle(options, "casesRoot"),
            ResolvedTestSuiteRoot = RequireSingle(options, "suitesRoot"),
            ResolvedTestPlanRoot = RequireSingle(options, "plansRoot")
        };

        var discovery = new DiscoveryService().Discover(roots);
        Console.WriteLine($"TestCases: {discovery.TestCases.Count}");
        Console.WriteLine($"TestSuites: {discovery.TestSuites.Count}");
        Console.WriteLine($"TestPlans: {discovery.TestPlans.Count}");
        return 0;
    }

    private static async Task<int> RunExecutionAsync(Dictionary<string, List<string>> options)
    {
        var target = RequireSingle(options, "target");
        var runRequest = new RunRequest();
        if (target == "testcase")
        {
            runRequest = new RunRequest
            {
                TestCase = RequireSingle(options, "id"),
                CaseInputs = ReadInputs(options)
            };
        }
        else if (target == "suite")
        {
            runRequest = new RunRequest
            {
                Suite = RequireSingle(options, "id")
            };
        }
        else if (target == "plan")
        {
            runRequest = new RunRequest
            {
                Plan = RequireSingle(options, "id")
            };
        }
        else
        {
            Console.Error.WriteLine("run target must be testcase|suite|plan.");
            return 1;
        }

        var roots = new DiscoveryRoots
        {
            ResolvedTestCaseRoot = RequireSingle(options, "casesRoot"),
            ResolvedTestSuiteRoot = RequireSingle(options, "suitesRoot"),
            ResolvedTestPlanRoot = RequireSingle(options, "plansRoot")
        };

        var runner = new TestCaseRunner(new ProcessRunner(), new GuidRunIdGenerator());
        var engine = new EngineRunner(runner);
        var context = new EngineRunContext
        {
            DiscoveryRoots = roots,
            RunRequest = runRequest,
            RunsRoot = RequireSingle(options, "runsRoot"),
            PowerShellPath = options.TryGetValue("pwsh", out var pwsh) ? pwsh[0] : "pwsh",
            GroupRunIdFactory = new GuidRunIdGenerator()
        };

        var summary = await engine.RunAsync(context, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Status: {summary.Status}");
        return 0;
    }

    private static Dictionary<string, InputValue>? ReadInputs(Dictionary<string, List<string>> options)
    {
        if (!options.TryGetValue("inputs", out var inputsValues))
        {
            return null;
        }

        var json = inputsValues[0];
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Inputs must be a JSON object.");
        }

        var result = new Dictionary<string, InputValue>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = JsonParsing.ParseInputValue(prop.Value);
        }

        return result;
    }

    private static Dictionary<string, List<string>> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                currentKey = arg[2..];
                if (!result.ContainsKey(currentKey))
                {
                    result[currentKey] = new List<string>();
                }
            }
            else if (currentKey is not null)
            {
                result[currentKey].Add(arg);
            }
        }

        return result;
    }

    private static string RequireSingle(Dictionary<string, List<string>> options, string key)
    {
        if (!options.TryGetValue(key, out var values) || values.Count == 0)
        {
            throw new ArgumentException($"Missing --{key}.");
        }

        return values[0];
    }
}
