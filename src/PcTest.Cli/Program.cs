using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;

var engine = new EngineService();

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    var command = args[0].ToLowerInvariant();
    switch (command)
    {
        case "discover":
            return HandleDiscover(args.Skip(1).ToArray());
        case "list":
            return HandleList(args.Skip(1).ToArray());
        case "run":
            return HandleRun(args.Skip(1).ToArray());
        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

int HandleDiscover(string[] options)
{
    var roots = ResolveRoots(options);
    var result = engine.Discover(roots);
    Console.WriteLine($"Discovered {result.Cases.Count} cases, {result.Suites.Count} suites, {result.Plans.Count} plans.");
    return 0;
}

int HandleList(string[] options)
{
    var roots = ResolveRoots(options);
    var discovery = engine.Discover(roots);
    if (options.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    var type = options[0].ToLowerInvariant();
    switch (type)
    {
        case "cases":
            foreach (var entry in discovery.Cases)
            {
                Console.WriteLine(entry.IdVersion);
            }
            return 0;
        case "suites":
            foreach (var entry in discovery.Suites)
            {
                Console.WriteLine(entry.IdVersion);
            }
            return 0;
        case "plans":
            foreach (var entry in discovery.Plans)
            {
                Console.WriteLine(entry.IdVersion);
            }
            return 0;
        default:
            PrintUsage();
            return 1;
    }
}

int HandleRun(string[] options)
{
    if (options.Length < 2)
    {
        PrintUsage();
        return 1;
    }

    var target = options[0].ToLowerInvariant();
    var idVersion = options[1];
    var roots = ResolveRoots(options.Skip(2).ToArray());
    var runRequest = LoadRunRequest(options.Skip(2).ToArray());

    switch (target)
    {
        case "case":
            var caseResult = engine.RunCase(roots, idVersion, runRequest);
            Console.WriteLine(JsonSerializer.Serialize(caseResult, JsonDefaults.Options));
            return 0;
        case "suite":
            var suiteResult = engine.RunSuite(roots, idVersion, runRequest);
            Console.WriteLine(JsonSerializer.Serialize(suiteResult, JsonDefaults.Options));
            return 0;
        case "plan":
            var planResult = engine.RunPlan(roots, idVersion, runRequest);
            Console.WriteLine(JsonSerializer.Serialize(planResult, JsonDefaults.Options));
            return 0;
        default:
            PrintUsage();
            return 1;
    }
}

ResolvedRoots ResolveRoots(string[] options)
{
    string? caseRoot = null;
    string? suiteRoot = null;
    string? planRoot = null;
    string? runsRoot = null;

    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--caseRoot":
                caseRoot = options[++i];
                break;
            case "--suiteRoot":
                suiteRoot = options[++i];
                break;
            case "--planRoot":
                planRoot = options[++i];
                break;
            case "--runsRoot":
                runsRoot = options[++i];
                break;
        }
    }

    var baseDir = Directory.GetCurrentDirectory();
    return new ResolvedRoots
    {
        TestCaseRoot = caseRoot ?? Path.Combine(baseDir, "assets", "TestCases"),
        TestSuiteRoot = suiteRoot ?? Path.Combine(baseDir, "assets", "TestSuites"),
        TestPlanRoot = planRoot ?? Path.Combine(baseDir, "assets", "TestPlans"),
        RunsRoot = runsRoot ?? Path.Combine(baseDir, "runs")
    };
}

RunRequest? LoadRunRequest(string[] options)
{
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--runRequest" && i + 1 < options.Length)
        {
            var path = options[i + 1];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RunRequest>(json, JsonDefaults.Options);
        }
    }

    return null;
}

void PrintUsage()
{
    Console.WriteLine("pctest discover --caseRoot <path> --suiteRoot <path> --planRoot <path>");
    Console.WriteLine("pctest list cases|suites|plans --caseRoot <path> --suiteRoot <path> --planRoot <path>");
    Console.WriteLine("pctest run case <id@version> [--runRequest path.json] --runsRoot <path>");
    Console.WriteLine("pctest run suite <id@version> [--runRequest path.json] --runsRoot <path>");
    Console.WriteLine("pctest run plan <id@version> [--runRequest path.json] --runsRoot <path>");
}

return 0;
