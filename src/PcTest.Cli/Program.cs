using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;

namespace PcTest.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pctest <discover|run> ...");
            return 1;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "discover" => RunDiscover(args.Skip(1).ToArray()),
                "run" => RunExecution(args.Skip(1).ToArray()),
                _ => throw new InvalidOperationException("Unknown command.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunDiscover(string[] args)
    {
        var options = ParseCommonOptions(args);
        var engine = new PcTestEngine(new RunnerService());
        var result = engine.Discover(options);
        Console.WriteLine($"Discovered {result.TestCases.Count} test cases, {result.Suites.Count} suites, {result.Plans.Count} plans.");
        return 0;
    }

    private static int RunExecution(string[] args)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Missing run target.");
        }

        var target = args[0].ToLowerInvariant();
        var options = ParseCommonOptions(args.Skip(1).ToArray());
        var runRequest = BuildRunRequest(target, args.Skip(1).ToArray());
        var engine = new PcTestEngine(new RunnerService());

        switch (target)
        {
            case "testcase":
                engine.RunTestCase(options, runRequest);
                break;
            case "suite":
                engine.RunSuite(options, runRequest);
                break;
            case "plan":
                engine.RunPlan(options, runRequest);
                break;
            default:
                throw new InvalidOperationException("Unknown run target.");
        }

        return 0;
    }

    private static EngineOptions ParseCommonOptions(string[] args)
    {
        string? cases = null;
        string? suites = null;
        string? plans = null;
        string? runs = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases":
                    cases = args[++i];
                    break;
                case "--suites":
                    suites = args[++i];
                    break;
                case "--plans":
                    plans = args[++i];
                    break;
                case "--runs":
                    runs = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(cases) || string.IsNullOrWhiteSpace(suites) || string.IsNullOrWhiteSpace(plans) || string.IsNullOrWhiteSpace(runs))
        {
            throw new InvalidOperationException("--cases, --suites, --plans, --runs are required.");
        }

        return new EngineOptions(cases, suites, plans, runs);
    }

    private static RunRequest BuildRunRequest(string target, string[] args)
    {
        string? id = null;
        string? inputsPath = null;
        string? envPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--id":
                    id = args[++i];
                    break;
                case "--inputs":
                    inputsPath = args[++i];
                    break;
                case "--env":
                    envPath = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("--id is required for run.");
        }

        var inputs = inputsPath is null ? null : LoadInputs(inputsPath);
        var env = envPath is null ? null : LoadEnv(envPath);

        return target switch
        {
            "testcase" => new RunRequest
            {
                TestCase = id,
                CaseInputs = inputs,
                EnvironmentOverrides = env is null ? null : new EnvironmentOverrides { Env = env }
            },
            "suite" => new RunRequest
            {
                Suite = id,
                EnvironmentOverrides = env is null ? null : new EnvironmentOverrides { Env = env }
            },
            "plan" => new RunRequest
            {
                Plan = id,
                EnvironmentOverrides = env is null ? null : new EnvironmentOverrides { Env = env }
            },
            _ => throw new InvalidOperationException("Unknown run target.")
        };
    }

    private static Dictionary<string, JsonElement> LoadInputs(string path)
    {
        using var doc = JsonUtils.ReadJsonDocument(path);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Inputs JSON must be an object.");
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }

        return result;
    }

    private static Dictionary<string, string> LoadEnv(string path)
    {
        using var doc = JsonUtils.ReadJsonDocument(path);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Env JSON must be an object.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return result;
    }
}
