namespace PcTest.Engine;

public sealed class ResolvedRoots
{
    public ResolvedRoots(string testCaseRoot, string testSuiteRoot, string testPlanRoot, string runsRoot)
    {
        TestCaseRoot = testCaseRoot;
        TestSuiteRoot = testSuiteRoot;
        TestPlanRoot = testPlanRoot;
        RunsRoot = runsRoot;
    }

    public string TestCaseRoot { get; }
    public string TestSuiteRoot { get; }
    public string TestPlanRoot { get; }
    public string RunsRoot { get; }
}
