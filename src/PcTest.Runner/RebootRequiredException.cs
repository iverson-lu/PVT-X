namespace PcTest.Runner;

public sealed class RebootRequiredException : Exception
{
    public RebootRequiredException(RebootRequest request, RunContext context, string caseRunFolder)
        : base($"Reboot required for run '{context.RunId}' to continue at phase {request.NextPhase}.")
    {
        Request = request;
        Context = context;
        CaseRunFolder = caseRunFolder;
    }

    public RebootRequest Request { get; }
    public RunContext Context { get; }
    public string CaseRunFolder { get; }
}
