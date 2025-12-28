using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    private static readonly Regex IdRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Identity Parse(string value)
    {
        if (!TryParse(value, out var identity, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return identity!;
    }

    public static bool TryParse(string value, out Identity? identity, out string? error)
    {
        identity = null;
        error = null;

        if (value is null)
        {
            error = "Identity is null.";
            return false;
        }

        var trimmed = value.Trim();
        if (!string.Equals(value, trimmed, StringComparison.Ordinal))
        {
            error = "Identity contains leading or trailing whitespace.";
            return false;
        }

        if (trimmed.Contains(' '))
        {
            error = "Identity contains whitespace.";
            return false;
        }

        var parts = trimmed.Split('@');
        if (parts.Length != 2)
        {
            error = "Identity must contain exactly one '@'.";
            return false;
        }

        var id = parts[0];
        var version = parts[1];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            error = "Identity id/version must be non-empty.";
            return false;
        }

        if (!IdRegex.IsMatch(id))
        {
            error = "Identity id contains invalid characters.";
            return false;
        }

        identity = new Identity(id, version);
        return true;
    }

    public override string ToString() => $"{Id}@{Version}";
}
