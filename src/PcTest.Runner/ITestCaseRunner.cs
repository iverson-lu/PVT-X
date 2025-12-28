using PcTest.Contracts.Models;

namespace PcTest.Runner;

public interface ITestCaseRunner
{
    Task<TestCaseRunResult> RunAsync(RunnerRequest request, CancellationToken cancellationToken = default);
}
