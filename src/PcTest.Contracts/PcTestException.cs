namespace PcTest.Contracts;

public sealed class PcTestException : Exception
{
    public PcTestException(string code, string message, IReadOnlyDictionary<string, object?>? payload = null)
        : base(message)
    {
        Code = code;
        Payload = payload ?? new Dictionary<string, object?>();
    }

    public string Code { get; }

    public IReadOnlyDictionary<string, object?> Payload { get; }
}
