using System.CommandLine;
using System.Linq;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Validation;
using PcTest.Engine;
using PcTest.Engine.Discovery;

namespace PcTest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--resume", StringComparison.OrdinalIgnoreCase)))
        {
            return await HandleResume(args);
        }

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
        runCommand.AddOption(runsRootOption);

        runCommand.SetHandler(HandleRun,
            targetOption, idOption, inputsOption, envOption,
            casesRootOption, suitesRootOption, plansRootOption, runsRootOption);

        rootCommand.AddCommand(runCommand);

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
        catch (PcTest.Runner.RebootRequestedException ex)
        {
            Console.WriteLine($"Reboot requested: {ex.Message}");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task<int> HandleResume(string[] args)
    {
        var runId = GetOptionValue(args, "--runId");
        var token = GetOptionValue(args, "--token");
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Resume requires --runId and --token");
            return 1;
        }

        var casesRoot = GetOptionValue(args, "--casesRoot") ?? "assets/TestCases";
        var suitesRoot = GetOptionValue(args, "--suitesRoot") ?? "assets/TestSuites";
        var plansRoot = GetOptionValue(args, "--plansRoot") ?? "assets/TestPlans";
        var runsRoot = GetOptionValue(args, "--runsRoot") ?? "Runs";

        var resolvedRunsRoot = ResolvePath(runsRoot);
        var session = PcTest.Runner.RebootResumeManager.LoadSession(resolvedRunsRoot, runId);
        if (!string.Equals(session.ResumeToken, token, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Invalid resume token");
            return 1;
        }

        session.ResumeCount++;
        if (session.ResumeCount > 1)
        {
            Console.Error.WriteLine("Resume loop detected. Aborting.");
            PcTest.Runner.RebootResumeManager.DeleteResumeTask(session.RunId);
            session.State = "Finalized";
            PcTest.Runner.RebootResumeManager.SaveSession(session);
            return 1;
        }

        session.State = "Resuming";
        PcTest.Runner.RebootResumeManager.SaveSession(session);

        var engine = new TestEngine();
        var resolvedCasesRoot = ResolvePath(session.CasesRoot ?? casesRoot);
        var resolvedSuitesRoot = ResolvePath(session.SuitesRoot ?? suitesRoot);
        var resolvedPlansRoot = ResolvePath(session.PlansRoot ?? plansRoot);
        var assetsRoot = Directory.GetParent(resolvedCasesRoot)?.FullName ?? "assets";

        engine.Configure(
            resolvedCasesRoot,
            resolvedSuitesRoot,
            resolvedPlansRoot,
            resolvedRunsRoot,
            assetsRoot);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Aborting...");
            cts.Cancel();
        };

        var result = await engine.ResumeAsync(session, cts.Token);
        Console.WriteLine("=== Resume Result ===");
        Console.WriteLine(JsonDefaults.Serialize(result));

        Environment.ExitCode = result.Status == PcTest.Contracts.RunStatus.Passed ? 0 : 1;
        return Environment.ExitCode;
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.Combine(Directory.GetCurrentDirectory(), path);
    }
}
