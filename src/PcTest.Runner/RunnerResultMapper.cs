using PcTest.Contracts.Models;

namespace PcTest.Runner;

public static class RunnerResultMapper
{
    public static TestCaseResult MapExitCode(TestCaseResult baseResult, int exitCode)
    {
        baseResult.ExitCode = exitCode;
        switch (exitCode)
        {
            case 0:
                baseResult.Status = RunStatus.Passed;
                break;
            case 1:
                baseResult.Status = RunStatus.Failed;
                break;
            default:
                baseResult.Status = RunStatus.Error;
                baseResult.Error = new RunError
                {
                    Type = "ScriptError",
                    Source = "Script",
                    Message = $"Script exit code {exitCode}."
                };
                break;
        }

        return baseResult;
    }
}
