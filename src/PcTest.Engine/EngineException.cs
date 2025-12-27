namespace PcTest.Engine;

public sealed class EngineException : Exception
{
    public string Code { get; }
    public Dictionary<string, object?> Payload { get; }

    public EngineException(string code, string message, Dictionary<string, object?> payload) : base(message)
    {
        Code = code;
        Payload = payload;
    }
}
