using System.Text.Json;

namespace PcTest.Contracts;

public sealed class PcTestException : Exception
{
    public PcTestException(string code, string message, object? payload = null)
        : base(message)
    {
        Code = code;
        Payload = payload;
    }

    public string Code { get; }
    public object? Payload { get; }
}

public static class ErrorPayload
{
    public static Dictionary<string, object?> SuiteRefInvalid(string suitePath, string refValue, string resolvedPath, string expectedRoot, string reason)
        => new(StringComparer.Ordinal)
        {
            ["entityType"] = "TestSuite",
            ["suitePath"] = suitePath,
            ["ref"] = refValue,
            ["resolvedPath"] = resolvedPath,
            ["expectedRoot"] = expectedRoot,
            ["reason"] = reason
        };

    public static Dictionary<string, object?> IdentityConflict(string entityType, string id, string version, IEnumerable<string> conflictPaths)
        => new(StringComparer.Ordinal)
        {
            ["entityType"] = entityType,
            ["id"] = id,
            ["version"] = version,
            ["conflictPaths"] = conflictPaths.ToArray()
        };

    public static Dictionary<string, object?> EnvRefResolveFailed(string parameterName, string? nodeId, string reason)
        => new(StringComparer.Ordinal)
        {
            ["parameter"] = parameterName,
            ["nodeId"] = nodeId,
            ["reason"] = reason
        };

    public static Dictionary<string, object?> IdentityResolutionFailed(string entityType, string id, string version, string reason, IEnumerable<string>? conflictPaths = null)
        => new(StringComparer.Ordinal)
        {
            ["entityType"] = entityType,
            ["id"] = id,
            ["version"] = version,
            ["reason"] = reason,
            ["conflictPaths"] = conflictPaths?.ToArray()
        };
}

public sealed record ErrorEvent(DateTimeOffset Timestamp, string Level, string Code, object? Payload)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonUtilities.SerializerOptions);
}
