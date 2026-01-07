using System.CommandLine;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;
using PcTest.Engine;
using PcTest.Engine.Execution;
using PcTest.Engine.Discovery;
using PcTest.Runner;

namespace PcTest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PC Test System CLI");

        // Common options
        var casesRootOption = new Option<string>(
            "--casesRoot",
            () => "assets/TestCases",
            "Path to TestCases root");

        var suitesRootOption = new Option<string>(
            "--suitesRoot",
            () => "assets/TestSuites",
            "Path to TestSuites root");

        var plansRootOption = new Option<string>(
            "--plansRoot",
            () => "assets/TestPlans",
            "Path to TestPlans root");

        var runsRootOption = new Option<string>(
            "--runsRoot",
            () => "Runs",
            "Path to Runs output root");

        var resumeOption = new Option<bool>(
            "--resume",
            "Resume a pending run after reboot");

        var resumeRunIdOption = new Option<string?>(
            "--runId",
            "Run ID to resume");

        var resumeTokenOption = new Option<string?>(
            "--token",
            "Resume token for validation");

        rootCommand.AddOption(resumeOption);
        rootCommand.AddOption(resumeRunIdOption);
        rootCommand.AddOption(resumeTokenOption);
        rootCommand.AddOption(runsRootOption);

        // Discover command
        var discoverCommand = new Command("discover", "Discover test cases, suites, and plans");
        discoverCommand.AddOption(casesRootOption);
        discoverCommand.AddOption(suitesRootOption);
        discoverCommand.AddOption(plansRootOption);
        discoverCommand.SetHandler(HandleDiscover, casesRootOption, suitesRootOption, plansRootOption);
        rootCommand.AddCommand(discoverCommand);

        // Run command with subcommands
        var runCommand = new Command("run", "Execute tests");

        // Target type option
        var targetOption = new Option<string>(
            "--target",
            "Target type: testcase, suite, or plan") { IsRequired = true };

        var idOption = new Option<string>(
            "--id",
            "Target identity (id@version)") { IsRequired = true };

        var inputsOption = new Option<string?>(
            "--inputs",
            "JSON object of inputs to override");

        var envOption = new Option<string?>(
            "--env",
            "JSON object of environment overrides");

        runCommand.AddOption(targetOption);
        runCommand.AddOption(idOption);
        runCommand.AddOption(inputsOption);
        runCommand.AddOption(envOption);
        runCommand.AddOption(casesRootOption);
        runCommand.AddOption(suitesRootOption);
        runCommand.AddOption(plansRootOption);

        runCommand.SetHandler(HandleRun,
            targetOption, idOption, inputsOption, envOption,
            casesRootOption, suitesRootOption, plansRootOption, runsRootOption);

        rootCommand.AddCommand(runCommand);

        rootCommand.SetHandler(HandleResume,
            resumeOption, resumeRunIdOption, resumeTokenOption, runsRootOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static void HandleDiscover(
        string casesRoot,
        string suitesRoot,
        string plansRoot)
    {
        try
        {
            var engine = new TestEngine();
            var resolvedCasesRoot = ResolvePath(casesRoot);
            var resolvedSuitesRoot = ResolvePath(suitesRoot);
            var resolvedPlansRoot = ResolvePath(plansRoot);
            var assetsRoot = Directory.GetParent(resolvedCasesRoot)?.FullName ?? "assets";
            
            engine.Configure(
                resolvedCasesRoot,
                resolvedSuitesRoot,
                resolvedPlansRoot,
                "Runs",
                assetsRoot);

            var discovery = engine.Discover();

            Console.WriteLine("=== Discovery Results ===");
            Console.WriteLine();

            Console.WriteLine($"Test Cases ({discovery.TestCases.Count}):");
            foreach (var tc in discovery.TestCases.Values.OrderBy(t => t.Identity))
            {
                Console.WriteLine($"  - {tc.Identity} ({tc.Manifest.Name})");
                Console.WriteLine($"    Path: {tc.FolderPath}");
                if (tc.Manifest.Parameters?.Count > 0)
                {
                    Console.WriteLine($"    Parameters: {string.Join(", ", tc.Manifest.Parameters.Select(p => p.Name))}");
                }
            }
            Console.WriteLine();

            Console.WriteLine($"Test Suites ({discovery.TestSuites.Count}):");
            foreach (var ts in discovery.TestSuites.Values.OrderBy(s => s.Identity))
            {
                Console.WriteLine($"  - {ts.Identity} ({ts.Manifest.Name})");
                Console.WriteLine($"    Path: {ts.FolderPath}");
                Console.WriteLine($"    Nodes: {string.Join(", ", ts.Manifest.TestCases.Select(n => n.NodeId))}");
            }
            Console.WriteLine();

            Console.WriteLine($"Test Plans ({discovery.TestPlans.Count}):");
            foreach (var tp in discovery.TestPlans.Values.OrderBy(p => p.Identity))
            {
                Console.WriteLine($"  - {tp.Identity} ({tp.Manifest.Name})");
                Console.WriteLine($"    Path: {tp.FolderPath}");
                Console.WriteLine($"    Suites: {string.Join(", ", tp.Manifest.Suites)}");
            }
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine($"Discovery failed: {ex.Message}");
            foreach (var error in ex.Result.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleRun(
        string target,
        string id,
        string? inputs,
        string? env,
        string casesRoot,
        string suitesRoot,
        string plansRoot,
        string runsRoot)
    {
        try
        {
            var engine = new TestEngine();
            var resolvedCasesRoot = ResolvePath(casesRoot);
            var resolvedSuitesRoot = ResolvePath(suitesRoot);
            var resolvedPlansRoot = ResolvePath(plansRoot);
            var resolvedRunsRoot = ResolvePath(runsRoot);
            var assetsRoot = Directory.GetParent(resolvedCasesRoot)?.FullName ?? "assets";
            
            engine.Configure(
                resolvedCasesRoot,
                resolvedSuitesRoot,
                resolvedPlansRoot,
                resolvedRunsRoot,
                assetsRoot);

            // Parse identity
            var parseResult = IdentityParser.Parse(id);
            if (!parseResult.Success)
            {
                Console.Error.WriteLine($"Invalid identity: {parseResult.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            // Build RunRequest
            var runRequest = new RunRequest();

            // Parse inputs
            Dictionary<string, JsonElement>? parsedInputs = null;
            if (!string.IsNullOrEmpty(inputs))
            {
                parsedInputs = JsonDefaults.Deserialize<Dictionary<string, JsonElement>>(inputs);
            }

            // Parse env overrides
            EnvironmentOverrides? envOverrides = null;
            if (!string.IsNullOrEmpty(env))
            {
                var envDict = JsonDefaults.Deserialize<Dictionary<string, string>>(env);
                if (envDict is not null)
                {
                    envOverrides = new EnvironmentOverrides { Env = envDict };
                }
            }

            runRequest.EnvironmentOverrides = envOverrides;

            switch (target.ToLowerInvariant())
            {
                case "testcase":
                    runRequest.TestCase = id;
                    runRequest.CaseInputs = parsedInputs;
                    break;

                case "suite":
                    runRequest.Suite = id;
                    if (parsedInputs is not null)
                    {
                        Console.Error.WriteLine("Note: For Suite runs, use --nodeOverrides instead of --inputs");
                    }
                    break;

                case "plan":
                    runRequest.Plan = id;
                    if (parsedInputs is not null)
                    {
                        Console.Error.WriteLine("Error: Plan runs cannot include input overrides");
                        Environment.ExitCode = 1;
                        return;
                    }
                    break;

                default:
                    Console.Error.WriteLine($"Unknown target type: {target}. Use: testcase, suite, or plan");
                    Environment.ExitCode = 1;
                    return;
            }

            Console.WriteLine($"Executing {target}: {id}");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("Aborting...");
                cts.Cancel();
            };

            var result = await engine.ExecuteAsync(runRequest, cts.Token);

            // Output result
            Console.WriteLine();
            Console.WriteLine("=== Execution Result ===");
            Console.WriteLine(JsonDefaults.Serialize(result));

            // Set exit code based on status
            if (result is PcTest.Contracts.Results.TestCaseResult tcr)
            {
                Environment.ExitCode = tcr.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
            }
            else if (result is PcTest.Contracts.Results.GroupResult gr)
            {
                Environment.ExitCode = gr.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
            }
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine($"Execution failed: {ex.Message}");
            foreach (var error in ex.Result.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleResume(
        bool resume,
        string? runId,
        string? token,
        string runsRoot)
    {
        if (!resume)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Resume requires --runId and --token.");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var resolvedRunsRoot = ResolvePath(runsRoot);
            var runFolder = Path.Combine(resolvedRunsRoot, runId);
            var session = await RebootResumeSession.LoadAsync(runFolder);

            if (!string.Equals(session.RunId, runId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Run ID mismatch in session.json.");
            }

            if (!string.Equals(session.ResumeToken, token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Resume token validation failed.");
            }

            if (!string.Equals(session.State, "PendingResume", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Session state '{session.State}' is not resumable.");
            }

            var resumeCount = session.ResumeCount + 1;
            if (resumeCount > 1)
            {
                ResumeTaskScheduler.DeleteResumeTask(runId);
                await session.WithState("Finalized", resumeCount).SaveAsync();
                throw new InvalidOperationException("Resume loop detected.");
            }

            await session.WithState("Resuming", resumeCount).SaveAsync();

            switch (session.EntityType)
            {
                case "TestCase":
                    await ResumeTestCaseAsync(session, resolvedRunsRoot, resumeCount);
                    break;
                case "TestSuite":
                    await ResumeSuiteAsync(session, resolvedRunsRoot, resumeCount);
                    break;
                case "TestPlan":
                    await ResumePlanAsync(session, resolvedRunsRoot, resumeCount);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown resume entity type '{session.EntityType}'.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Resume failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.Combine(Directory.GetCurrentDirectory(), path);
    }

    private static void AppendIndexEntryIfMissing(
        string runsRoot,
        string runId,
        TestCaseResult result,
        ResumeRunContext resumeContext)
    {
        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        if (File.Exists(indexPath))
        {
            foreach (var line in File.ReadLines(indexPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonDefaults.Deserialize<IndexEntry>(line);
                    if (entry is not null && string.Equals(entry.RunId, runId, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch
                {
                    // Ignore malformed lines.
                }
            }
        }

        var folderManager = new GroupRunFolderManager(runsRoot);
        folderManager.AppendIndexEntry(new IndexEntry
        {
            RunId = runId,
            RunType = RunType.TestCase,
            NodeId = resumeContext.NodeId,
            TestId = result.TestId,
            TestVersion = result.TestVersion,
            SuiteId = resumeContext.SuiteId,
            SuiteVersion = resumeContext.SuiteVersion,
            PlanId = resumeContext.PlanId,
            PlanVersion = resumeContext.PlanVersion,
            ParentRunId = resumeContext.ParentRunId,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status
        });
    }

    private static async Task ResumeTestCaseAsync(
        RebootResumeSession session,
        string resolvedRunsRoot,
        int resumeCount)
    {
        if (session.Context is null)
        {
            throw new InvalidOperationException("Resume context missing for test case.");
        }

        var context = ResumeContextConverter.ToRunContext(session.Context, session.Context.RunId, session.NextPhase, true);
        using var cts = new CancellationTokenSource();
        var runner = new TestCaseRunner(cts.Token);
        PcTest.Contracts.Results.TestCaseResult? result = null;

        try
        {
            result = await runner.ExecuteAsync(context);
        }
        finally
        {
            ResumeTaskScheduler.DeleteResumeTask(session.RunId);
            await session.WithState("Finalized", resumeCount).SaveAsync();
        }

        if (result is not null)
        {
            AppendIndexEntryIfMissing(resolvedRunsRoot, session.RunId, result, session.Context);
            Console.WriteLine("=== Resume Result ===");
            Console.WriteLine(JsonDefaults.Serialize(result));
            Environment.ExitCode = result.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
        }
    }

    private static async Task ResumeSuiteAsync(
        RebootResumeSession session,
        string resolvedRunsRoot,
        int resumeCount)
    {
        if (session.Context is null)
        {
            throw new InvalidOperationException("Resume context missing for suite.");
        }

        var discovery = BuildDiscovery(session);
        var runFolder = session.RunFolder;
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        var runRequestPath = Path.Combine(runFolder, "runRequest.json");
        var manifestSnapshot = LoadJson<GroupManifestSnapshot>(manifestPath);
        var runRequest = LoadJson<RunRequest>(runRequestPath);

        if (manifestSnapshot.SuiteId is null || manifestSnapshot.SuiteVersion is null)
        {
            throw new InvalidOperationException("Suite manifest snapshot missing identity.");
        }

        var suiteIdentity = $"{manifestSnapshot.SuiteId}@{manifestSnapshot.SuiteVersion}";
        if (!discovery.TestSuites.TryGetValue(suiteIdentity, out var suite))
        {
            throw new InvalidOperationException($"Suite '{suiteIdentity}' not found for resume.");
        }

        using var cts = new CancellationTokenSource();
        var orchestrator = new SuiteOrchestrator(discovery, resolvedRunsRoot, session.AssetsRoot, NullExecutionReporter.Instance, cts.Token);
        GroupResult? result = null;

        try
        {
            result = await orchestrator.ResumeAsync(
                suite,
                runRequest,
                session);
        }
        finally
        {
            ResumeTaskScheduler.DeleteResumeTask(session.RunId);
            await session.WithState("Finalized", resumeCount).SaveAsync();
        }

        if (result is not null)
        {
            Console.WriteLine("=== Resume Result ===");
            Console.WriteLine(JsonDefaults.Serialize(result));
            Environment.ExitCode = result.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
        }
    }

    private static async Task ResumePlanAsync(
        RebootResumeSession session,
        string resolvedRunsRoot,
        int resumeCount)
    {
        if (session.Context is null)
        {
            throw new InvalidOperationException("Resume context missing for plan.");
        }

        var discovery = BuildDiscovery(session);
        var runFolder = session.RunFolder;
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        var runRequestPath = Path.Combine(runFolder, "runRequest.json");
        var manifestSnapshot = LoadJson<GroupManifestSnapshot>(manifestPath);
        var runRequest = LoadJson<RunRequest>(runRequestPath);

        if (manifestSnapshot.PlanId is null || manifestSnapshot.PlanVersion is null)
        {
            throw new InvalidOperationException("Plan manifest snapshot missing identity.");
        }

        var planIdentity = $"{manifestSnapshot.PlanId}@{manifestSnapshot.PlanVersion}";
        if (!discovery.TestPlans.TryGetValue(planIdentity, out var plan))
        {
            throw new InvalidOperationException($"Plan '{planIdentity}' not found for resume.");
        }

        using var cts = new CancellationTokenSource();
        var orchestrator = new PlanOrchestrator(discovery, resolvedRunsRoot, session.AssetsRoot, NullExecutionReporter.Instance, cts.Token);
        GroupResult? result = null;

        try
        {
            result = await orchestrator.ResumeAsync(plan, runRequest, session);
        }
        finally
        {
            ResumeTaskScheduler.DeleteResumeTask(session.RunId);
            await session.WithState("Finalized", resumeCount).SaveAsync();
        }

        if (result is not null)
        {
            Console.WriteLine("=== Resume Result ===");
            Console.WriteLine(JsonDefaults.Serialize(result));
            Environment.ExitCode = result.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
        }
    }

    private static DiscoveryResult BuildDiscovery(RebootResumeSession session)
    {
        if (string.IsNullOrWhiteSpace(session.CasesRoot)
            || string.IsNullOrWhiteSpace(session.SuitesRoot)
            || string.IsNullOrWhiteSpace(session.PlansRoot))
        {
            throw new InvalidOperationException("Resume session missing discovery roots.");
        }

        var service = new DiscoveryService();
        return service.Discover(session.CasesRoot, session.SuitesRoot, session.PlansRoot);
    }

    private static T LoadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing required file: {path}");
        }

        var json = File.ReadAllText(path);
        var data = JsonDefaults.Deserialize<T>(json);
        if (data is null)
        {
            throw new InvalidOperationException($"Failed to parse {path}.");
        }

        return data;
    }
}
