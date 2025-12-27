namespace PcTest.Contracts;

public sealed record RunEvent(DateTime Timestamp, string Code, Dictionary<string, object?> Data);
