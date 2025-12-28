using PcTest.Contracts;
using PcTest.Engine;

static int Main(string[] args)
{
    if (args.Length == 0)
    {
        Console.WriteLine("Usage: pctest <discover|run> [...]");
        return 1;
    }

    var command = args[0];
    var options = BuildOptions(args.Skip(1).ToArray());
    var orchestrator = new EngineOrchestrator();

    switch (command)
    {
        case "discover":
            var discovery = orchestrator.Discover(options);
            Console.WriteLine($"Discovered {discovery.TestCases.Count} test cases, {discovery.TestSuites.Count} suites, {discovery.TestPlans.Count} plans.");
            return 0;
        case "run":
            return RunCommand(orchestrator, options, args.Skip(1).ToArray());
        default:
            Console.WriteLine($"Unknown command {command}.");
            return 1;
    }
}

static int RunCommand(EngineOrchestrator orchestrator, EngineOptions options, string[] args)
{
    if (args.Length == 0)
    {
        Console.WriteLine("Usage: pctest run <testCase|suite|plan> --id <id@version> [--runRequest path]");
        return 1;
    }

    var mode = args[0];
    var (id, runRequestPath) = ParseRunArgs(args.Skip(1).ToArray());
    var runRequest = runRequestPath is not null
        ? JsonUtilities.ReadFile<RunRequest>(runRequestPath)
        : new RunRequest();

    switch (mode)
    {
        case "testCase":
            runRequest = new RunRequest
            {
                TestCase = id,
                CaseInputs = runRequest.CaseInputs,
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            };
            orchestrator.RunTestCase(options, runRequest);
            Console.WriteLine("TestCase run completed.");
            return 0;
        case "suite":
            runRequest = new RunRequest
            {
                Suite = id,
                NodeOverrides = runRequest.NodeOverrides,
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            };
            orchestrator.RunSuite(options, runRequest);
            Console.WriteLine("Suite run completed.");
            return 0;
        case "plan":
            runRequest = new RunRequest
            {
                Plan = id,
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            };
            orchestrator.RunPlan(options, runRequest);
            Console.WriteLine("Plan run completed.");
            return 0;
        default:
            Console.WriteLine($"Unknown run target {mode}.");
            return 1;
    }
}

static (string Id, string? RunRequestPath) ParseRunArgs(string[] args)
{
    string? id = null;
    string? runRequestPath = null;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--id":
                id = args[++i];
                break;
            case "--runRequest":
                runRequestPath = args[++i];
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(id))
    {
        throw new PcTestException("RunRequest.Invalid", "--id is required.");
    }

    return (id, runRequestPath);
}

static EngineOptions BuildOptions(string[] args)
{
    var testCaseRoot = "assets/TestCases";
    var testSuiteRoot = "assets/TestSuites";
    var testPlanRoot = "assets/TestPlans";
    var runsRoot = "Runs";

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--testCases":
                testCaseRoot = args[++i];
                break;
            case "--testSuites":
                testSuiteRoot = args[++i];
                break;
            case "--testPlans":
                testPlanRoot = args[++i];
                break;
            case "--runs":
                runsRoot = args[++i];
                break;
        }
    }

    return new EngineOptions
    {
        TestCaseRoot = testCaseRoot,
        TestSuiteRoot = testSuiteRoot,
        TestPlanRoot = testPlanRoot,
        RunsRoot = runsRoot
    };
}
