using PcTest.Contracts;

namespace PcTest.Engine;

public static class IndexWriter
{
    private static readonly object Lock = new();

    public static void AppendTestCase(string runsRoot, TestCaseExecutionResult result, TestCaseManifest manifest, string? nodeId, Identity? suiteIdentity, Identity? planIdentity, string? parentRunId)
    {
        var entry = new Dictionary<string, object?>
        {
            ["runId"] = result.RunId,
            ["runType"] = "TestCase",
            ["testId"] = manifest.Id,
            ["testVersion"] = manifest.Version,
            ["startTime"] = result.StartTime.ToString("O"),
            ["endTime"] = result.EndTime.ToString("O"),
            ["status"] = result.Status
        };

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            entry["nodeId"] = nodeId;
        }

        if (suiteIdentity.HasValue)
        {
            entry["suiteId"] = suiteIdentity.Value.Id;
            entry["suiteVersion"] = suiteIdentity.Value.Version;
        }

        if (planIdentity.HasValue)
        {
            entry["planId"] = planIdentity.Value.Id;
            entry["planVersion"] = planIdentity.Value.Version;
        }

        if (!string.IsNullOrWhiteSpace(parentRunId))
        {
            entry["parentRunId"] = parentRunId;
        }

        AppendEntry(runsRoot, entry);
    }

    public static void AppendSuite(string runsRoot, SuiteRunSummary summary, string? planRunId)
    {
        var entry = new Dictionary<string, object?>
        {
            ["runId"] = summary.RunId,
            ["runType"] = "TestSuite",
            ["suiteId"] = summary.SuiteIdentity.Id,
            ["suiteVersion"] = summary.SuiteIdentity.Version,
            ["startTime"] = summary.StartTime.ToString("O"),
            ["endTime"] = summary.EndTime.ToString("O"),
            ["status"] = summary.Status
        };

        if (summary.PlanIdentity is not null)
        {
            entry["planId"] = summary.PlanIdentity.Value.Id;
            entry["planVersion"] = summary.PlanIdentity.Value.Version;
        }

        if (!string.IsNullOrWhiteSpace(planRunId))
        {
            entry["parentRunId"] = planRunId;
        }

        AppendEntry(runsRoot, entry);
    }

    public static void AppendPlan(string runsRoot, PlanRunSummary summary)
    {
        var entry = new Dictionary<string, object?>
        {
            ["runId"] = summary.RunId,
            ["runType"] = "TestPlan",
            ["planId"] = summary.PlanIdentity.Id,
            ["planVersion"] = summary.PlanIdentity.Version,
            ["startTime"] = summary.StartTime.ToString("O"),
            ["endTime"] = summary.EndTime.ToString("O"),
            ["status"] = summary.Status
        };

        AppendEntry(runsRoot, entry);
    }

    private static void AppendEntry(string runsRoot, Dictionary<string, object?> entry)
    {
        var path = Path.Combine(runsRoot, "index.jsonl");
        lock (Lock)
        {
            JsonUtils.AppendJsonLine(path, entry);
        }
    }
}
