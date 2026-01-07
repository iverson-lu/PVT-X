namespace PcTest.Runner;

public sealed class RebootControlRequest
{
    public string Type { get; set; } = string.Empty;
    public int NextPhase { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RebootControlOptions? Reboot { get; set; }
}

public sealed class RebootControlOptions
{
    public int? DelaySec { get; set; }
}
