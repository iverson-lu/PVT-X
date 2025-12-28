namespace PcTest.Contracts;

public static class RunIdGenerator
{
    public static string NewRunId(string prefix = "R")
    {
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }
}
