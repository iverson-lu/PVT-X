using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Models;
using PcTest.Engine;

namespace PcTest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pc-test <discover|run> ...");
            return 1;
        }

        try
        {
            var command = args[0];
            return command switch
            {
                "discover" => Discover(args.Skip(1).ToArray()),
                "run" => await RunAsync(args.Skip(1).ToArray()),
                _ => throw new ArgumentException($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Discover(string[] args)
    {
        var (testCaseRoot, suiteRoot, planRoot, _) = ParseCommonRoots(args);
        var discovery = new DiscoveryService().Discover(testCaseRoot, suiteRoot, planRoot);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            testCases = discovery.TestCases.Select(x => x.Identity.ToString()).ToArray(),
            suites = discovery.Suites.Select(x => x.Identity.ToString()).ToArray(),
            plans = discovery.Plans.Select(x => x.Identity.ToString()).ToArray()
        }, JsonUtil.SerializerOptions));
        return 0;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Missing run target.");
        }

        var target = args[0];
        var (testCaseRoot, suiteRoot, planRoot, runsRoot) = ParseCommonRoots(args.Skip(1).ToArray());
        var discovery = new DiscoveryService().Discover(testCaseRoot, suiteRoot, planRoot);
        var engine = new EngineRunner(discovery, runsRoot);

        var request = new RunRequest
        {
            EnvironmentOverrides = new EnvironmentOverrides { Env = ParseEnv(args) }
        };

        var inputs = ParseInputs(args);
        if (target == "testcase")
        {
            request.TestCase = GetArg(args, "--id") ?? throw new ArgumentException("--id required");
            request.CaseInputs = inputs;
            await engine.RunTestCaseAsync(request);
            return 0;
        }

        if (target == "suite")
        {
            request.Suite = GetArg(args, "--id") ?? throw new ArgumentException("--id required");
            request.NodeOverrides = ParseNodeOverrides(args);
            await engine.RunSuiteAsync(request);
            return 0;
        }

        if (target == "plan")
        {
            request.Plan = GetArg(args, "--id") ?? throw new ArgumentException("--id required");
            await engine.RunPlanAsync(request);
            return 0;
        }

        throw new ArgumentException($"Unknown run target: {target}");
    }

    private static (string testCaseRoot, string suiteRoot, string planRoot, string runsRoot) ParseCommonRoots(string[] args)
    {
        var testCaseRoot = GetArg(args, "--testCases") ?? "assets/TestCases";
        var suiteRoot = GetArg(args, "--testSuites") ?? "assets/TestSuites";
        var planRoot = GetArg(args, "--testPlans") ?? "assets/TestPlans";
        var runsRoot = GetArg(args, "--runs") ?? "Runs";
        return (testCaseRoot, suiteRoot, planRoot, runsRoot);
    }

    private static string? GetArg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        return args[index + 1];
    }

    private static Dictionary<string, string> ParseEnv(string[] args)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--env=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pair = arg[6..];
            var split = pair.Split('=', 2);
            if (split.Length == 2)
            {
                env[split[0]] = split[1];
            }
        }

        return env;
    }

    private static Dictionary<string, JsonElement>? ParseInputs(string[] args)
    {
        var inputs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--input=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pair = arg[8..];
            var split = pair.Split('=', 2);
            if (split.Length == 2)
            {
                var json = split[1];
                var doc = JsonDocument.Parse(json);
                inputs[split[0]] = doc.RootElement.Clone();
            }
        }

        return inputs.Count > 0 ? inputs : null;
    }

    private static Dictionary<string, NodeOverride>? ParseNodeOverrides(string[] args)
    {
        var overrides = new Dictionary<string, NodeOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--node-input=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pair = arg[13..];
            var split = pair.Split('=', 3);
            if (split.Length == 3)
            {
                var nodeId = split[0];
                var inputName = split[1];
                var doc = JsonDocument.Parse(split[2]);
                if (!overrides.TryGetValue(nodeId, out var nodeOverride))
                {
                    nodeOverride = new NodeOverride { Inputs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) };
                    overrides[nodeId] = nodeOverride;
                }

                nodeOverride.Inputs![inputName] = doc.RootElement.Clone();
            }
        }

        return overrides.Count > 0 ? overrides : null;
    }
}
