using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    private static readonly Regex IdentityPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Identity Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Identity is required.");
        }

        var trimmed = value.Trim();
        var parts = trimmed.Split('@');
        if (parts.Length != 2)
        {
            throw new InvalidDataException("Identity must be in id@version format.");
        }

        if (!IdentityPattern.IsMatch(parts[0]))
        {
            throw new InvalidDataException("Identity id contains invalid characters.");
        }

        if (string.IsNullOrWhiteSpace(parts[1]) || parts[1].Contains(' '))
        {
            throw new InvalidDataException("Identity version is invalid.");
        }

        return new Identity(parts[0], parts[1]);
    }

    public override string ToString() => $"{Id}@{Version}";
}
