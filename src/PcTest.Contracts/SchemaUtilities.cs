namespace PcTest.Contracts;

public static class SchemaUtilities
{
    public const string CurrentVersion = "1.5.0";

    public static void EnsureSchemaVersion(string schemaVersion, string context)
    {
        if (!string.Equals(schemaVersion, CurrentVersion, StringComparison.Ordinal))
        {
            throw new PcTestException("Schema.Version.Invalid", $"{context} schemaVersion {schemaVersion} is not supported.");
        }
    }
}
