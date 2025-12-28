namespace PcTest.Contracts;

public sealed class ValidationError
{
    public ValidationError(string code, string message, IReadOnlyDictionary<string, object?>? payload = null)
    {
        Code = code;
        Message = message;
        Payload = payload ?? new Dictionary<string, object?>();
    }

    public string Code { get; }
    public string Message { get; }
    public IReadOnlyDictionary<string, object?> Payload { get; }
}

public sealed class ValidationResult<T>
{
    private ValidationResult(T? value, IReadOnlyList<ValidationError> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }
    public IReadOnlyList<ValidationError> Errors { get; }
    public bool IsSuccess => Errors.Count == 0 && Value is not null;

    public static ValidationResult<T> Success(T value) => new(value, Array.Empty<ValidationError>());
    public static ValidationResult<T> Failure(params ValidationError[] errors) => new(default, errors);
    public static ValidationResult<T> Failure(IEnumerable<ValidationError> errors) => new(default, errors.ToArray());
}
