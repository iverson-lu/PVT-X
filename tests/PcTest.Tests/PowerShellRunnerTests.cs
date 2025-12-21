using PcTest.Runner.Process;
using Xunit;

namespace PcTest.Tests;

public class PowerShellRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesArgumentList()
    {
        var fakeInvoker = new FakeProcessInvoker();
        var runner = new PowerShellRunner(fakeInvoker);

        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();

        await runner.RunAsync("pwsh", "script.ps1", new[] { "-Name", "Value" }, "work", TimeSpan.FromSeconds(5), stdout, stderr, CancellationToken.None);

        Assert.NotNull(fakeInvoker.Request);
        Assert.Equal("pwsh", fakeInvoker.Request!.FileName);
        Assert.Equal("work", fakeInvoker.Request!.WorkingDirectory);
        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "script.ps1", "-Name", "Value" }, fakeInvoker.Request!.Arguments);
    }

    [Fact]
    public void ProcessInvoker_ValidatesRequest()
    {
        var invoker = new ProcessInvoker();
        var request = new ProcessStartRequest
        {
            FileName = string.Empty,
            Arguments = Array.Empty<string>(),
            Timeout = TimeSpan.FromSeconds(1)
        };

        Assert.Throws<ArgumentException>(() => invoker.RunAsync(request).GetAwaiter().GetResult());
    }

    private sealed class FakeProcessInvoker : IProcessInvoker
    {
        public ProcessStartRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ProcessResult(0, false, string.Empty, string.Empty));
        }
    }
}
