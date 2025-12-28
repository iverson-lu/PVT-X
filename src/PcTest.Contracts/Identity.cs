using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public readonly record struct Identity(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

public static class IdentityParser
{
    private static readonly Regex IdRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Identity Parse(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var trimmed = input.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@'))
        {
            throw new ValidationException("Identity.Parse.Invalid", new Dictionary<string, object>
            {
                ["input"] = input
            });
        }

        var id = trimmed[..at];
        var version = trimmed[(at + 1)..];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new ValidationException("Identity.Parse.Invalid", new Dictionary<string, object>
            {
                ["input"] = input
            });
        }

        if (!IdRegex.IsMatch(id))
        {
            throw new ValidationException("Identity.Parse.Invalid", new Dictionary<string, object>
            {
                ["input"] = input,
                ["reason"] = "InvalidCharacters"
            });
        }

        if (trimmed.Contains(' '))
        {
            throw new ValidationException("Identity.Parse.Invalid", new Dictionary<string, object>
            {
                ["input"] = input,
                ["reason"] = "Whitespace"
            });
        }

        return new Identity(id, version);
    }
}
