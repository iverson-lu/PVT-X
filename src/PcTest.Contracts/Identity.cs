namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    public static Identity Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Identity is required.", nameof(text));
        }

        int at = text.LastIndexOf('@');
        if (at <= 0 || at == text.Length - 1)
        {
            throw new FormatException($"Identity '{text}' must be in id@version format.");
        }

        string id = text[..at];
        string version = text[(at + 1)..];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new FormatException($"Identity '{text}' must include id and version.");
        }

        return new Identity(id, version);
    }

    public override string ToString() => $"{Id}@{Version}";
}
