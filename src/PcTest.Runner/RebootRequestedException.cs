namespace PcTest.Runner;

public sealed class RebootRequestedException : Exception
{
    public RebootRequestedException(RebootControlRequest request)
        : base($"Reboot requested for next phase {request.NextPhase}: {request.Reason}")
    {
        Request = request;
    }

    public RebootControlRequest Request { get; }
}
