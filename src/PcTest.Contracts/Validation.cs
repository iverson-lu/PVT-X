namespace PcTest.Contracts;

public sealed class ValidationException : Exception
{
    public ValidationException(string code, IReadOnlyDictionary<string, object> payload)
        : base(code)
    {
        Code = code;
        Payload = payload;
    }

    public ValidationException(IEnumerable<ValidationError> errors)
        : base("ValidationFailed")
    {
        Errors = errors.ToArray();
        Code = "ValidationFailed";
        Payload = new Dictionary<string, object>();
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Payload { get; }
    public IReadOnlyList<ValidationError> Errors { get; } = Array.Empty<ValidationError>();
}

public sealed record ValidationError(string Code, IReadOnlyDictionary<string, object> Payload);
