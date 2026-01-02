namespace PcTest.Contracts;

/// <summary>
/// Privilege levels for test execution.
/// </summary>
public enum Privilege
{
    User,
    AdminPreferred,
    AdminRequired
}

/// <summary>
/// Execution status for test runs.
/// </summary>
public enum RunStatus
{
    Passed,
    Failed,
    Error,
    Timeout,
    Aborted
}

/// <summary>
/// Type of run entity.
/// </summary>
public enum RunType
{
    TestCase,
    TestSuite,
    TestPlan
}

/// <summary>
/// Error types for result classification.
/// </summary>
public enum ErrorType
{
    Timeout,
    ScriptError,
    RunnerError,
    Aborted
}

/// <summary>
/// Supported parameter types per spec section 6.2.
/// Array types are NOT supported - use json type for complex structures.
/// </summary>
public static class ParameterTypes
{
    public const string Int = "int";
    public const string Double = "double";
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Path = "path";
    public const string File = "file";
    public const string Folder = "folder";
    public const string Enum = "enum";
    public const string Json = "json";

    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal)
    {
        Int, Double, String, Boolean, Path, File, Folder, Enum, Json
    };

    public static bool IsValid(string type) => ValidTypes.Contains(type);
}
