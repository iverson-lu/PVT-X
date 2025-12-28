using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public readonly struct Identity : IEquatable<Identity>
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Identity(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }
    public string Version { get; }

    public static bool TryParse(string? value, out Identity identity, out string? error)
    {
        identity = default;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Identity is empty.";
            return false;
        }

        string trimmed = value.Trim();
        int at = trimmed.IndexOf('@');
        if (at < 0 || at != trimmed.LastIndexOf('@'))
        {
            error = "Identity must contain exactly one @ separator.";
            return false;
        }

        string id = trimmed[..at];
        string version = trimmed[(at + 1)..];
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
        {
            error = "Identity id or version is empty.";
            return false;
        }

        if (id.Contains(' ', StringComparison.Ordinal) || version.Contains(' ', StringComparison.Ordinal))
        {
            error = "Identity contains whitespace.";
            return false;
        }

        if (!IdPattern.IsMatch(id))
        {
            error = "Identity id has invalid characters.";
            return false;
        }

        identity = new Identity(id, version);
        return true;
    }

    public override string ToString() => $"{Id}@{Version}";

    public bool Equals(Identity other) => string.Equals(Id, other.Id, StringComparison.Ordinal) && string.Equals(Version, other.Version, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Identity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Version);
}
