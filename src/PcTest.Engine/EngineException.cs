namespace PcTest.Engine;

public sealed class EngineException : Exception
{
    public EngineException(string code, object payload)
        : base(code)
    {
        Code = code;
        Payload = payload;
    }

    public string Code { get; }
    public object Payload { get; }
}
