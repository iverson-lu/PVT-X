using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

public static class IdentityParser
{
    private static readonly Regex IdRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Identity Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Identifier is required.");
        }

        var trimmed = value.Trim();
        var parts = trimmed.Split('@');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Identifier must be in id@version format.");
        }

        var id = parts[0];
        var version = parts[1];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("Identifier must include id and version.");
        }

        if (id.Any(char.IsWhiteSpace) || version.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("Identifier cannot include whitespace.");
        }

        if (!IdRegex.IsMatch(id))
        {
            throw new InvalidOperationException("Identifier has invalid characters.");
        }

        return new Identity(id, version);
    }
}
