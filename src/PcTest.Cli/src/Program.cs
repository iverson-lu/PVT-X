using PcTest.Engine.Execution;

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

        var command = args[0].ToLowerInvariant();
        var executor = new TestExecutor();

        try
        {
            return command switch
            {
                "discover" => HandleDiscover(executor, args.Skip(1)),
                "run" => await HandleRunAsync(executor, args.Skip(1)),
                _ => ShowUnknown()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int HandleDiscover(TestExecutor executor, IEnumerable<string> args)
    {
        var parsed = ParseCommon(args);
        if (parsed.root is null)
        {
            Console.Error.WriteLine("--root is required for discover");
            return 1;
        }

        var tests = executor.Discover(parsed.root);
        foreach (var test in tests)
        {
            Console.WriteLine($"{test.Id}\t{test.Name}\t{test.Version}\t{test.Category}");
        }

        return 0;
    }

    private static async Task<int> HandleRunAsync(TestExecutor executor, IEnumerable<string> args)
    {
        var parsed = ParseRun(args);
        if (parsed.root is null || parsed.id is null)
        {
            Console.Error.WriteLine("--root and --id are required for run");
            return 1;
        }

        var result = await executor.RunAsync(parsed.root, parsed.id, parsed.parameters, parsed.runsRoot);
        Console.WriteLine($"Status: {result.Result.Status}");
        Console.WriteLine($"Run Folder: {result.RunFolder}");
        return 0;
    }

    private static (string? root, Dictionary<string, string> parameters, string? id, string? runsRoot) ParseRun(IEnumerable<string> args)
    {
        var root = (string?)null;
        var id = (string?)null;
        string? runsRoot = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            switch (current)
            {
                case "--root":
                    root = MoveNextOrThrow(enumerator, "--root requires a value");
                    break;
                case "--id":
                    id = MoveNextOrThrow(enumerator, "--id requires a value");
                    break;
                case "--param":
                    var raw = MoveNextOrThrow(enumerator, "--param requires Name=Value");
                    var split = raw.Split('=', 2);
                    if (split.Length != 2)
                    {
                        throw new ArgumentException("--param must be provided as Name=Value");
                    }

                    parameters[split[0]] = split[1];
                    break;
                case "--runs":
                    runsRoot = MoveNextOrThrow(enumerator, "--runs requires a value");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{current}'");
            }
        }

        return (root, parameters, id, runsRoot);
    }

    private static (string? root, Dictionary<string, string> parameters) ParseCommon(IEnumerable<string> args)
    {
        var root = (string?)null;
        var parameters = new Dictionary<string, string>();
        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            switch (current)
            {
                case "--root":
                    root = MoveNextOrThrow(enumerator, "--root requires a value");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{current}'");
            }
        }

        return (root, parameters);
    }

    private static string MoveNextOrThrow(IEnumerator<string> enumerator, string error)
    {
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException(error);
        }

        return enumerator.Current;
    }

    private static int ShowUnknown()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pctest discover --root <TestCasesRoot>");
        Console.WriteLine("pctest run --root <TestCasesRoot> --id <TestId> [--param Name=Value ...] [--runs <RunsRoot>]");
    }
}
