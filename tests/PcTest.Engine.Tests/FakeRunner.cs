using PcTest.Runner;

namespace PcTest.Engine.Tests;

public sealed class FakeRunner : ICaseRunner
{
    public List<RunCaseRequest> Requests { get; } = new();

    public RunCaseResult Run(RunCaseRequest request)
    {
        Requests.Add(request);
        return new RunCaseResult
        {
            RunId = $"R-{Guid.NewGuid():N}",
            RunFolder = Path.Combine(request.RunsRoot, "fake"),
            Status = "Passed",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow
        };
    }
}
