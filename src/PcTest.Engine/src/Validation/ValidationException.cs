namespace PcTest.Engine.Validation;

/// <summary>
/// Represents parameter validation failures.
/// </summary>
public class ValidationException : Exception
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

    /// <summary>
    /// Initializes a new instance with a message and inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public ValidationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
