using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;
using PcTest.Engine.Execution;

namespace PcTest.Engine;

/// <summary>
/// Main Engine facade for discovery and execution.
/// </summary>
public sealed class TestEngine
{
    private DiscoveryResult? _discovery;
    private string _testCaseRoot = string.Empty;
    private string _testSuiteRoot = string.Empty;
    private string _testPlanRoot = string.Empty;
    private string _runsRoot = string.Empty;
    private string _assetsRoot = string.Empty;
    private IExecutionReporter _reporter = NullExecutionReporter.Instance;

    /// <summary>
    /// Sets the execution reporter for progress notifications.
    /// </summary>
    public void SetReporter(IExecutionReporter reporter)
    {
        _reporter = reporter ?? NullExecutionReporter.Instance;
    }

    /// <summary>
    /// Configures the resolved roots.
    /// </summary>
    public void Configure(
        string testCaseRoot,
        string testSuiteRoot,
        string testPlanRoot,
        string runsRoot,
        string assetsRoot)
    {
        _testCaseRoot = PathUtils.NormalizePath(testCaseRoot);
        _testSuiteRoot = PathUtils.NormalizePath(testSuiteRoot);
        _testPlanRoot = PathUtils.NormalizePath(testPlanRoot);
        _runsRoot = PathUtils.NormalizePath(runsRoot);
        _assetsRoot = PathUtils.NormalizePath(assetsRoot);
    }

    /// <summary>
    /// Discovers all entities under the configured roots.
    /// </summary>
    public DiscoveryResult Discover()
    {
        var service = new DiscoveryService();
        _discovery = service.Discover(_testCaseRoot, _testSuiteRoot, _testPlanRoot);
        return _discovery;
    }

    /// <summary>
    /// Gets the current discovery result.
    /// </summary>
    public DiscoveryResult? Discovery => _discovery;

    /// <summary>
    /// Executes a RunRequest.
    /// </summary>
    public async Task<object> ExecuteAsync(RunRequest runRequest, CancellationToken cancellationToken = default)
    {
        if (_discovery is null)
        {
            _discovery = Discover();
        }

        var targetType = runRequest.TargetType;
        var targetIdentity = runRequest.TargetIdentity;

        if (targetType is null || string.IsNullOrEmpty(targetIdentity))
        {
            throw new ValidationException(ErrorCodes.RunRequestInvalidFormat,
                "RunRequest must specify exactly one of: suite, testCase, or plan");
        }

        // Parse and validate identity
        var parseResult = IdentityParser.Parse(targetIdentity);
        if (!parseResult.Success)
        {
            throw new ValidationException(ErrorCodes.RunRequestInvalidFormat,
                parseResult.ErrorMessage ?? "Invalid identity format");
        }

        switch (targetType)
        {
            case RunType.TestCase:
                return await ExecuteStandaloneTestCaseAsync(targetIdentity, runRequest, cancellationToken);

            case RunType.TestSuite:
                return await ExecuteSuiteAsync(targetIdentity, runRequest, cancellationToken);

            case RunType.TestPlan:
                return await ExecutePlanAsync(targetIdentity, runRequest, cancellationToken);

            default:
                throw new ValidationException(ErrorCodes.RunRequestInvalidFormat,
                    $"Unknown target type: {targetType}");
        }
    }

    /// <summary>
    /// Resumes a rebooted Test Case run using a persisted session.
    /// </summary>
    public async Task<TestCaseResult> ResumeAsync(ResumeSession session, CancellationToken cancellationToken = default)
    {
        if (_discovery is null)
        {
            _discovery = Discover();
        }

        if (!string.Equals(session.EntityType, "TestCase", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(ErrorCodes.RunRequestInvalidFormat,
                $"Resume is only supported for TestCase runs. Found: {session.EntityType}");
        }

        if (!_discovery.TestCases.TryGetValue(session.EntityId, out var testCase))
        {
            throw new ValidationException(new ValidationError
            {
                Code = ErrorCodes.RunRequestIdentityNotFound,
                Message = $"TestCase '{session.EntityId}' not found",
                EntityType = "TestCase",
                Id = IdentityParser.Parse(session.EntityId).Id,
                Version = IdentityParser.Parse(session.EntityId).Version,
                Reason = "NotFound"
            });
        }

        var executor = new StandaloneCaseExecutor(_discovery, _runsRoot, _assetsRoot, _reporter, cancellationToken);
        return await executor.ExecuteResumeAsync(testCase, session);
    }

    /// <summary>
    /// Executes a standalone Test Case by identity.
    /// </summary>
    public async Task<TestCaseResult> ExecuteStandaloneTestCaseAsync(
        string identity,
        RunRequest runRequest,
        CancellationToken cancellationToken = default)
    {
        if (_discovery is null)
        {
            _discovery = Discover();
        }

        if (!_discovery.TestCases.TryGetValue(identity, out var testCase))
        {
            throw new ValidationException(new ValidationError
            {
                Code = ErrorCodes.RunRequestIdentityNotFound,
                Message = $"TestCase '{identity}' not found",
                EntityType = "TestCase",
                Id = IdentityParser.Parse(identity).Id,
                Version = IdentityParser.Parse(identity).Version,
                Reason = "NotFound"
            });
        }

        var executor = new StandaloneCaseExecutor(_discovery, _runsRoot, _assetsRoot, _reporter, cancellationToken);
        return await executor.ExecuteAsync(testCase, runRequest);
    }

    /// <summary>
    /// Executes a Test Suite by identity.
    /// </summary>
    public async Task<GroupResult> ExecuteSuiteAsync(
        string identity,
        RunRequest runRequest,
        CancellationToken cancellationToken = default)
    {
        if (_discovery is null)
        {
            _discovery = Discover();
        }

        if (!_discovery.TestSuites.TryGetValue(identity, out var suite))
        {
            throw new ValidationException(new ValidationError
            {
                Code = ErrorCodes.RunRequestIdentityNotFound,
                Message = $"TestSuite '{identity}' not found",
                EntityType = "TestSuite",
                Id = IdentityParser.Parse(identity).Id,
                Version = IdentityParser.Parse(identity).Version,
                Reason = "NotFound"
            });
        }

        var orchestrator = new SuiteOrchestrator(_discovery, _runsRoot, _assetsRoot, _reporter, cancellationToken);
        return await orchestrator.ExecuteAsync(suite, runRequest);
    }

    /// <summary>
    /// Executes a Test Plan by identity.
    /// </summary>
    public async Task<GroupResult> ExecutePlanAsync(
        string identity,
        RunRequest runRequest,
        CancellationToken cancellationToken = default)
    {
        if (_discovery is null)
        {
            _discovery = Discover();
        }

        if (!_discovery.TestPlans.TryGetValue(identity, out var plan))
        {
            throw new ValidationException(new ValidationError
            {
                Code = ErrorCodes.RunRequestIdentityNotFound,
                Message = $"TestPlan '{identity}' not found",
                EntityType = "TestPlan",
                Id = IdentityParser.Parse(identity).Id,
                Version = IdentityParser.Parse(identity).Version,
                Reason = "NotFound"
            });
        }

        var orchestrator = new PlanOrchestrator(_discovery, _runsRoot, _assetsRoot, _reporter, cancellationToken);
        return await orchestrator.ExecuteAsync(plan, runRequest);
    }
}
