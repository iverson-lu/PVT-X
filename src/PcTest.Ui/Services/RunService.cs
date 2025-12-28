using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Engine;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for executing test runs.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly TestEngine _engine;
    private readonly ISettingsService _settingsService;
    private RunExecutionContext? _currentContext;
    private RunExecutionState? _currentState;

    public event EventHandler<RunExecutionState>? StateChanged;
    public event EventHandler<string>? ConsoleOutput;

    public RunExecutionState? CurrentState => _currentState;

    public RunService(TestEngine engine, ISettingsService settingsService)
    {
        _engine = engine;
        _settingsService = settingsService;
    }

    public async Task<RunExecutionContext> ExecuteAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        
        // Configure engine
        _engine.Configure(
            settings.ResolvedTestCasesRoot,
            settings.ResolvedTestSuitesRoot,
            settings.ResolvedTestPlansRoot,
            settings.ResolvedRunsRoot);
        
        // Create execution context
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runType = request.TargetType ?? RunType.TestCase;
        var targetIdentity = request.TargetIdentity ?? string.Empty;
        
        _currentContext = new RunExecutionContext
        {
            RunId = GenerateRunId(runType),
            RunType = runType,
            TargetIdentity = targetIdentity,
            StartTime = DateTime.UtcNow,
            CancellationSource = cts
        };
        
        // Initialize state
        _currentState = new RunExecutionState
        {
            RunId = _currentContext.RunId,
            IsRunning = true
        };
        
        StateChanged?.Invoke(this, _currentState);
        
        try
        {
            // Execute through engine
            var result = await _engine.ExecuteAsync(request, cts.Token);
            
            // Update final state
            _currentState.IsRunning = false;
            _currentState.FinalStatus = GetStatusFromResult(result);
            StateChanged?.Invoke(this, _currentState);
        }
        catch (OperationCanceledException)
        {
            _currentState.IsRunning = false;
            _currentState.FinalStatus = RunStatus.Aborted;
            StateChanged?.Invoke(this, _currentState);
        }
        catch (Exception ex)
        {
            _currentState.IsRunning = false;
            _currentState.FinalStatus = RunStatus.Error;
            ConsoleOutput?.Invoke(this, $"Error: {ex.Message}");
            StateChanged?.Invoke(this, _currentState);
        }
        
        return _currentContext;
    }

    public Task StopAsync(string runId)
    {
        if (_currentContext?.RunId == runId)
        {
            // Request graceful cancellation
            _currentContext.CancellationSource.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task AbortAsync(string runId)
    {
        if (_currentContext?.RunId == runId)
        {
            // Force immediate cancellation
            _currentContext.CancellationSource.Cancel();
        }
        return Task.CompletedTask;
    }

    private static string GenerateRunId(RunType runType)
    {
        var prefix = runType switch
        {
            RunType.TestCase => "R",
            RunType.TestSuite => "S",
            RunType.TestPlan => "P",
            _ => "R"
        };
        
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..35];
    }

    private static RunStatus GetStatusFromResult(object result)
    {
        // Extract status from various result types
        var statusProp = result.GetType().GetProperty("Status");
        if (statusProp?.GetValue(result) is RunStatus status)
        {
            return status;
        }
        return RunStatus.Passed;
    }
}
