using System.Text.Json;

namespace PcTest.Contracts.Validation;

/// <summary>
/// Validation error with structured payload.
/// </summary>
public sealed class ValidationError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? Id { get; init; }
    public string? Version { get; init; }
    public string? Location { get; init; }
    public List<string>? ConflictPaths { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, object?>? Data { get; init; }

    public override string ToString()
    {
        var parts = new List<string> { $"[{Code}] {Message}" };
        if (!string.IsNullOrEmpty(Location))
            parts.Add($"at {Location}");
        if (ConflictPaths is { Count: > 0 })
            parts.Add($"conflicts: [{string.Join(", ", ConflictPaths)}]");
        if (!string.IsNullOrEmpty(Reason))
            parts.Add($"reason: {Reason}");
        return string.Join(" ", parts);
    }
}

/// <summary>
/// Validation result container.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; } = new();
    public List<ValidationError> Warnings { get; } = new();

    public void AddError(ValidationError error) => Errors.Add(error);
    public void AddWarning(ValidationError warning) => Warnings.Add(warning);

    public void Merge(ValidationResult other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
    }

    public static ValidationResult Success() => new();

    public static ValidationResult Failure(ValidationError error)
    {
        var result = new ValidationResult();
        result.AddError(error);
        return result;
    }

    public static ValidationResult Failure(string code, string message, string? location = null)
    {
        return Failure(new ValidationError { Code = code, Message = message, Location = location });
    }
}

/// <summary>
/// Exception thrown when validation fails.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationResult Result { get; }

    public ValidationException(ValidationResult result)
        : base(result.Errors.Count > 0 ? result.Errors[0].ToString() : "Validation failed")
    {
        Result = result;
    }

    public ValidationException(ValidationError error)
        : this(ValidationResult.Failure(error))
    {
    }

    public ValidationException(string code, string message, string? location = null)
        : this(new ValidationError { Code = code, Message = message, Location = location })
    {
    }
}
