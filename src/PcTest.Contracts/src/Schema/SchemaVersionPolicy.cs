namespace PcTest.Contracts.Schema;

/// <summary>
/// Centralized schema version support policy for manifests and results.
/// </summary>
public static class SchemaVersionPolicy
{
    private const int SupportedMajor = 1;
    private const string SupportedRange = "1.x";

    /// <summary>
    /// Ensures a manifest schema version is supported.
    /// </summary>
    /// <param name="schemaVersion">Schema version string from the manifest.</param>
    /// <param name="path">Optional manifest path for diagnostics.</param>
    /// <exception cref="InvalidDataException">Thrown when the schema version is unsupported.</exception>
    public static void EnsureManifestSupported(string? schemaVersion, string? path)
    {
        EnsureSupported(schemaVersion, path, "manifest");
    }

    /// <summary>
    /// Gets the schema version to stamp on generated results.
    /// </summary>
    /// <returns>Schema version string.</returns>
    public static string ResultSchemaVersion() => $"{SupportedMajor}.0";

    private static void EnsureSupported(string? schemaVersion, string? path, string kind)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new InvalidDataException(BuildMessage(path, "(missing)", kind));
        }

        if (!Version.TryParse(schemaVersion, out var parsed))
        {
            throw new InvalidDataException(BuildMessage(path, schemaVersion, kind));
        }

        if (parsed.Major != SupportedMajor)
        {
            throw new InvalidDataException(BuildMessage(path, schemaVersion, kind));
        }
    }

    private static string BuildMessage(string? path, string schemaVersion, string kind)
    {
        var location = string.IsNullOrWhiteSpace(path) ? string.Empty : $"{Path.GetFullPath(path)}: ";
        return $"{location}{kind} schemaVersion '{schemaVersion}' is not supported. Supported range: {SupportedRange}.";
    }
}
