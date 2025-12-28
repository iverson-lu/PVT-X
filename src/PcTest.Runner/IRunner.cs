namespace PcTest.Runner;

public interface IRunner
{
    Task<RunnerResult> RunTestCaseAsync(RunnerRequest request, CancellationToken cancellationToken);
}
