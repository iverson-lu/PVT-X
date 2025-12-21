namespace PcTest.Engine.Validation;

/// <summary>
/// Represents parameter validation failures.
/// </summary>
public class ValidationException : InvalidDataException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    public ValidationException()
    {
    }

    /// <summary>
    /// Initializes a new instance with a message.
    /// </summary>
    /// <param name="message">Error message.</param>
    public ValidationException(string? message) : base(message)
    {
    }
}
