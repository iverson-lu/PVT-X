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
    public const string IntArray = "int[]";
    public const string DoubleArray = "double[]";
    public const string StringArray = "string[]";
    public const string BooleanArray = "boolean[]";
    public const string PathArray = "path[]";
    public const string FileArray = "file[]";
    public const string FolderArray = "folder[]";
    public const string EnumArray = "enum[]";

    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal)
    {
        Int, Double, String, Boolean, Path, File, Folder, Enum,
        IntArray, DoubleArray, StringArray, BooleanArray, PathArray, FileArray, FolderArray, EnumArray
    };

    public static bool IsValid(string type) => ValidTypes.Contains(type);
    
    public static bool IsArrayType(string type) => type.EndsWith("[]", StringComparison.Ordinal);
    
    public static string GetElementType(string arrayType)
    {
        if (!IsArrayType(arrayType))
            return arrayType;
        return arrayType[..^2];
    }
}
