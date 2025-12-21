using PcTest.Contracts.Manifest;
using PcTest.Engine.Discovery;
using PcTest.Engine.Validation;
using PcTest.Runner.Execution;

namespace PcTest.Engine.Execution;

/// <summary>
/// Coordinates test discovery and execution.
/// </summary>
public class TestExecutor
{
    private readonly TestDiscoveryService _discovery = new();
    private readonly TestRunner _runner = new();

    /// <summary>
    /// Discovers tests located under the given root directory.
    /// </summary>
    /// <param name="root">Root directory to search for tests.</param>
    /// <returns>Discovered test metadata entries.</returns>
    public IEnumerable<DiscoveredTest> Discover(string root)
    {
        return _discovery.Discover(root);
    }

    /// <summary>
    /// Executes a test by id under the specified root.
    /// </summary>
    /// <param name="root">Root directory containing test manifests.</param>
    /// <param name="testId">Identifier of the test to run.</param>
    /// <param name="parameters">Parameter values supplied by the user.</param>
    /// <param name="runsRoot">Optional root folder for storing run artifacts.</param>
    /// <param name="cancellationToken">Cancellation token for the execution.</param>
    /// <returns>Run response containing artifact paths and result data.</returns>
    public async Task<TestRunResponse> RunAsync(string root, string testId, IDictionary<string, string> parameters, string? runsRoot = null, CancellationToken cancellationToken = default)
    {
        var manifestPath = ResolveManifestPath(root, testId);
        var manifest = ManifestLoader.Load(manifestPath);
        ManifestValidator.Validate(manifest);
        PrivilegeEnforcer.EnsureAllowed(manifest.Privilege);

        var boundParameters = ParameterBinder.Bind(manifest, parameters);
        var scriptPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "run.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("run.ps1 not found next to manifest", scriptPath);
        }

        var request = new TestRunRequest
        {
            Manifest = manifest,
            ManifestPath = manifestPath,
            ScriptPath = scriptPath,
            Parameters = boundParameters,
            RunsRoot = runsRoot ?? Path.Combine(Environment.CurrentDirectory, "Runs")
        };

        return await _runner.RunAsync(request, cancellationToken);
    }

    private string ResolveManifestPath(string root, string testId)
    {
        var match = _discovery.Discover(root).FirstOrDefault(t => string.Equals(t.Id, testId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new InvalidOperationException($"Test id '{testId}' not found under root '{root}'.");
        }

        return match.ManifestPath;
    }
}
