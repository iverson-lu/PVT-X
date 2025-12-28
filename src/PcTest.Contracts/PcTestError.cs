using System.Text.Json;

namespace PcTest.Contracts;

public sealed record PcTestError(string Code, string Message, JsonElement? Payload = null);

public sealed class PcTestException : Exception
{
    public PcTestException(IReadOnlyList<PcTestError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<PcTestError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<PcTestError> errors)
    {
        if (errors.Count == 0)
        {
            return "PcTest error.";
        }

        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}"));
    }
}
