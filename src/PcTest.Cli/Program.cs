using PcTest.Contracts;
using PcTest.Engine;

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }
        var key = args[i][2..];
        if (i + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for {args[i]}.");
        }
        options[key] = args[i + 1];
        i++;
    }
    return options;
}

if (args.Length == 0)
{
    Console.WriteLine("Usage: pc-test <discover|run> ...");
    return;
}

var command = args[0];
if (command.Equals("discover", StringComparison.OrdinalIgnoreCase))
{
    var options = ParseOptions(args.Skip(1).ToArray());
    var roots = new DiscoveryRoots
    {
        TestCaseRoot = options["caseRoot"],
        TestSuiteRoot = options["suiteRoot"],
        TestPlanRoot = options["planRoot"]
    };
    var outputPath = options.TryGetValue("output", out var value) ? value : "discovery.json";
    new PcTestEngine().DiscoverToFile(roots, outputPath);
    Console.WriteLine($"Discovery written to {outputPath}.");
    return;
}

if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        throw new InvalidOperationException("Missing run type.");
    }
    var runType = args[1];
    var options = ParseOptions(args.Skip(2).ToArray());
    var roots = new DiscoveryRoots
    {
        TestCaseRoot = options["caseRoot"],
        TestSuiteRoot = options["suiteRoot"],
        TestPlanRoot = options["planRoot"]
    };
    var discovery = new DiscoveryService().Discover(roots);
    if (discovery.HasErrors)
    {
        throw new InvalidOperationException("Discovery failed.");
    }
    var runRequest = JsonUtilities.ReadJson<RunRequest>(options["request"]);
    var engineOptions = new EngineRunOptions
    {
        RunsRoot = options["runsRoot"],
        TestCaseRoot = roots.TestCaseRoot,
        Discovery = discovery
    };
    var engine = new PcTestEngine();

    if (runType.Equals("testCase", StringComparison.OrdinalIgnoreCase))
    {
        engine.RunStandaloneTestCase(engineOptions, runRequest);
        return;
    }
    if (runType.Equals("suite", StringComparison.OrdinalIgnoreCase))
    {
        engine.RunSuite(engineOptions, runRequest);
        return;
    }
    if (runType.Equals("plan", StringComparison.OrdinalIgnoreCase))
    {
        engine.RunPlan(engineOptions, runRequest);
        return;
    }

    throw new InvalidOperationException("Unknown run type.");
}

throw new InvalidOperationException("Unknown command.");
