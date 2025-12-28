using System.Text.RegularExpressions;

namespace PcTest.Contracts.Validation;

/// <summary>
/// Identity parsing utilities per spec section 8.1.
/// </summary>
public static partial class IdentityParser
{
    // Allowed characters for id: [A-Za-z0-9._-]+
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled)]
    private static partial Regex IdPattern();

    /// <summary>
    /// Result of parsing an identity string.
    /// </summary>
    public sealed class ParseResult
    {
        public bool Success { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string? ErrorMessage { get; init; }

        public static ParseResult Fail(string message) => new() { Success = false, ErrorMessage = message };

        public static ParseResult Ok(string id, string version) => new() { Success = true, Id = id, Version = version };
    }

    /// <summary>
    /// Parses an identity string in the format id@version.
    /// Per spec section 8.1:
    /// - Exactly one @ separator
    /// - Leading/trailing whitespace trimmed
    /// - No internal whitespace
    /// - id must match [A-Za-z0-9._-]+
    /// - Case-sensitive match
    /// </summary>
    public static ParseResult Parse(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return ParseResult.Fail("Identity string is null or empty");
        }

        var trimmed = identity.Trim();

        // Check for internal whitespace
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return ParseResult.Fail("Identity string must not contain internal whitespace");
        }

        var parts = trimmed.Split('@');
        if (parts.Length != 2)
        {
            return ParseResult.Fail("Identity string must contain exactly one '@' separator");
        }

        var id = parts[0];
        var version = parts[1];

        if (string.IsNullOrEmpty(id))
        {
            return ParseResult.Fail("Id part is empty");
        }

        if (string.IsNullOrEmpty(version))
        {
            return ParseResult.Fail("Version part is empty");
        }

        if (!IdPattern().IsMatch(id))
        {
            return ParseResult.Fail($"Id '{id}' contains invalid characters. Allowed: [A-Za-z0-9._-]+");
        }

        return ParseResult.Ok(id, version);
    }

    /// <summary>
    /// Validates if an identity string is well-formed.
    /// </summary>
    public static bool IsValid(string? identity) => Parse(identity).Success;

    /// <summary>
    /// Creates an identity string from id and version.
    /// </summary>
    public static string Create(string id, string version) => $"{id}@{version}";
}
