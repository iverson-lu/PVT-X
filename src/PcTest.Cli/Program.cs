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
            PrintUsage();
            return 1;
        }

        try
        {
            var command = args[0];
            return command switch
            {
                "discover" => HandleDiscover(args.Skip(1).ToArray()),
                "run" => await HandleRun(args.Skip(1).ToArray()),
                _ => PrintUsageWithError($"Unknown command '{command}'.")
            };
        }
        catch (EngineException ex)
        {
            Console.Error.WriteLine($"Error {ex.Code}: {ex.Message}");
            Console.Error.WriteLine(JsonSerializer.Serialize(ex.Payload, JsonUtilities.SerializerOptions));
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 3;
        }
    }

    private static int HandleDiscover(string[] args)
    {
        var roots = ParseRoots(args);
        var engine = CreateEngine();
        var result = engine.Discover(roots);
        var payload = new
        {
            testCases = result.TestCases.Select(tc => new { id = tc.Identity.Id, version = tc.Identity.Version, path = tc.ManifestPath }),
            suites = result.Suites.Select(suite => new { id = suite.Identity.Id, version = suite.Identity.Version, path = suite.ManifestPath }),
            plans = result.Plans.Select(plan => new { id = plan.Identity.Id, version = plan.Identity.Version, path = plan.ManifestPath })
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonUtilities.SerializerOptions));
        return 0;
    }

    private static async Task<int> HandleRun(string[] args)
    {
        if (args.Length < 2)
        {
            return PrintUsageWithError("run requires target type and id@version.");
        }

        var targetType = args[0];
        var targetId = args[1];
        var roots = ParseRoots(args.Skip(2).ToArray());
        var runRequestPath = ReadOption(args, "--runRequest");
        RunRequest runRequest;
        if (!string.IsNullOrWhiteSpace(runRequestPath))
        {
            runRequest = JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(runRequestPath), JsonUtilities.SerializerOptions)
                         ?? throw new InvalidOperationException("RunRequest invalid.");
        }
        else
        {
            runRequest = targetType switch
            {
                "testCase" => new RunRequest { TestCase = targetId },
                "suite" => new RunRequest { Suite = targetId },
                "plan" => new RunRequest { Plan = targetId },
                _ => throw new InvalidOperationException($"Unknown run target '{targetType}'.")
            };
        }

        var engine = CreateEngine();
        var runId = await engine.RunAsync(roots, runRequest, CancellationToken.None);
        Console.WriteLine(runId);
        return 0;
    }

    private static EngineService CreateEngine()
    {
        var runner = new TestCaseRunner(new PowerShellExecutor());
        return new EngineService(runner);
    }

    private static EngineRoots ParseRoots(string[] args)
    {
        var caseRoot = ReadOption(args, "--caseRoot") ?? throw new InvalidOperationException("--caseRoot required.");
        var suiteRoot = ReadOption(args, "--suiteRoot") ?? throw new InvalidOperationException("--suiteRoot required.");
        var planRoot = ReadOption(args, "--planRoot") ?? throw new InvalidOperationException("--planRoot required.");
        var runsRoot = ReadOption(args, "--runsRoot") ?? throw new InvalidOperationException("--runsRoot required.");

        return new EngineRoots(
            PathUtilities.NormalizePath(caseRoot),
            PathUtilities.NormalizePath(suiteRoot),
            PathUtilities.NormalizePath(planRoot),
            PathUtilities.NormalizePath(runsRoot));
    }

    private static string? ReadOption(IEnumerable<string> args, string name)
    {
        var list = args.ToList();
        var index = list.FindIndex(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= list.Count)
        {
            return null;
        }

        return list[index + 1];
    }

    private static int PrintUsageWithError(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  discover --caseRoot <path> --suiteRoot <path> --planRoot <path> --runsRoot <path>");
        Console.WriteLine("  run testCase <id@version> --caseRoot <path> --suiteRoot <path> --planRoot <path> --runsRoot <path> [--runRequest <path>]");
        Console.WriteLine("  run suite <id@version> --caseRoot <path> --suiteRoot <path> --planRoot <path> --runsRoot <path> [--runRequest <path>]");
        Console.WriteLine("  run plan <id@version> --caseRoot <path> --suiteRoot <path> --planRoot <path> --runsRoot <path> [--runRequest <path>]");
    }
}
