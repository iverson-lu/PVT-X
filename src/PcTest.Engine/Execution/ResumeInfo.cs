namespace PcTest.Engine.Execution;

public sealed class ResumeInfo
{
    public string RunId { get; init; } = string.Empty;
    public int Phase { get; init; }
    public string? RunFolderPath { get; init; }
    public bool AppendOutput { get; init; } = true;
}
