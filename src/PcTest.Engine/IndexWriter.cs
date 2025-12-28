using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public static class IndexWriter
{
    private static readonly SemaphoreSlim Mutex = new(1, 1);

    public static async Task AppendTestCaseAsync(string runsRoot, CaseRunResult result, CaseRunRequest request, Identity? suite, Identity? plan)
    {
        object entry = new
        {
            runId = result.RunId,
            runType = "TestCase",
            nodeId = request.NodeId,
            testId = request.TestId,
            testVersion = request.TestVersion,
            suiteId = suite?.Id,
            suiteVersion = suite?.Version,
            planId = plan?.Id,
            planVersion = plan?.Version,
            parentRunId = request.ParentRunId,
            startTime = result.StartTime.ToString("O", CultureInfo.InvariantCulture),
            endTime = result.EndTime.ToString("O", CultureInfo.InvariantCulture),
            status = result.Status
        };

        await AppendAsync(runsRoot, entry);
    }

    public static async Task AppendSuiteAsync(string runsRoot, string runId, ResolvedTestSuite suite, Identity? plan, DateTimeOffset start, DateTimeOffset end, string status)
    {
        object entry = new
        {
            runId,
            runType = "TestSuite",
            suiteId = suite.Identity.Id,
            suiteVersion = suite.Identity.Version,
            planId = plan?.Id,
            planVersion = plan?.Version,
            startTime = start.ToString("O", CultureInfo.InvariantCulture),
            endTime = end.ToString("O", CultureInfo.InvariantCulture),
            status
        };

        await AppendAsync(runsRoot, entry);
    }

    public static async Task AppendPlanAsync(string runsRoot, string runId, ResolvedTestPlan plan, DateTimeOffset start, DateTimeOffset end, string status)
    {
        object entry = new
        {
            runId,
            runType = "TestPlan",
            planId = plan.Identity.Id,
            planVersion = plan.Identity.Version,
            startTime = start.ToString("O", CultureInfo.InvariantCulture),
            endTime = end.ToString("O", CultureInfo.InvariantCulture),
            status
        };

        await AppendAsync(runsRoot, entry);
    }

    private static async Task AppendAsync(string runsRoot, object entry)
    {
        string path = Path.Combine(runsRoot, "index.jsonl");
        string line = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        await Mutex.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine);
        }
        finally
        {
            Mutex.Release();
        }
    }
}
