using PcTest.Contracts;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

/// <summary>
/// Tests for PowerShellExecutor per spec section 9.1.
/// Per spec section 11: Exit 0 => Passed, Exit 1 => Failed, Other => Error
/// </summary>
public class PowerShellExecutorTests : IDisposable
{
    private readonly string _tempRoot;

    public PowerShellExecutorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTestPS_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ExitZero_ReturnsPassedStatus()
    {
        // Spec section 11: Exit 0 => Passed
        var scriptPath = CreateScript("exit 0");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Equal(RunStatus.Passed, result.Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Execute_ExitOne_ReturnsFailedStatus()
    {
        // Spec section 11: Exit 1 => Failed
        var scriptPath = CreateScript("exit 1");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Execute_ExitOther_ReturnsErrorStatus()
    {
        // Spec section 11: Exit other => Error
        var scriptPath = CreateScript("exit 42");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Equal(RunStatus.Error, result.Status);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public async Task Execute_Timeout_ReturnsTimeoutStatus()
    {
        // Spec section 11: Timeout handling
        var scriptPath = CreateScript("Start-Sleep -Seconds 30");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 1);

        Assert.Equal(RunStatus.Timeout, result.Status);
        Assert.True(result.WasTimeout);
    }

    [Fact]
    public async Task Execute_CancellationToken_Aborts()
    {
        // Spec section 11: Cancellation => Abort
        var scriptPath = CreateScript("Start-Sleep -Seconds 30");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var executor = new PowerShellExecutor(cts.Token);
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 60);

        Assert.Equal(RunStatus.Aborted, result.Status);
        Assert.True(result.WasAborted);
    }

    [Fact]
    public async Task Execute_ParametersPassed_CorrectFormat()
    {
        // Spec section 9: Parameters passed via ProcessStartInfo.ArgumentList
        // Create script that echoes parameters
        var scriptPath = CreateScript(@"
param(
    [int]$Duration,
    [string]$Mode
)
Write-Host ""Duration=$Duration Mode=$Mode""
exit 0
");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();
        var inputs = new Dictionary<string, object?>
        {
            ["Duration"] = 42,
            ["Mode"] = "test-mode"
        };

        var result = await executor.ExecuteAsync(
            scriptPath,
            inputs,
            env,
            _tempRoot,
            timeoutSec: 30);

        // Verify script executed successfully with parameters
        Assert.Equal(RunStatus.Passed, result.Status);
        Assert.Contains("Duration=42", result.Stdout);
        Assert.Contains("Mode=test-mode", result.Stdout);
    }

    [Fact]
    public async Task Execute_Environment_SetCorrectly()
    {
        // Create script that reads environment variable
        var scriptPath = CreateScript(@"
if ($env:TEST_VAR -eq 'test-value') { exit 0 } else { exit 1 }
");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string> { ["TEST_VAR"] = "test-value" };

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Equal(RunStatus.Passed, result.Status);
    }

    [Fact]
    public async Task Execute_StdoutStderr_Captured()
    {
        var scriptPath = CreateScript(@"
Write-Host 'stdout message'
Write-Error 'stderr message'
exit 0
");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();

        var result = await executor.ExecuteAsync(
            scriptPath,
            new Dictionary<string, object?>(),
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Contains("stdout message", result.Stdout);
        Assert.Contains("stderr message", result.Stderr);
    }

    [Fact]
    public async Task Execute_NullParameter_OmittedFromCommandLine()
    {
        // Spec section 9: Missing optional parameters MUST be omitted
        var scriptPath = CreateScript(@"
param(
    [int]$Required,
    [int]$Optional = 99
)
Write-Host ""Optional=$Optional""
exit 0
");
        var executor = new PowerShellExecutor();
        var env = new Dictionary<string, string>();
        var inputs = new Dictionary<string, object?>
        {
            ["Required"] = 42,
            ["Optional"] = null // Should be omitted, default used
        };

        var result = await executor.ExecuteAsync(
            scriptPath,
            inputs,
            env,
            _tempRoot,
            timeoutSec: 30);

        Assert.Equal(RunStatus.Passed, result.Status);
        Assert.Contains("Optional=99", result.Stdout); // Default from script
    }

    private string CreateScript(string content)
    {
        var scriptPath = Path.Combine(_tempRoot, $"script_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, content);
        return scriptPath;
    }
}
