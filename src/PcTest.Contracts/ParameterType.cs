namespace PcTest.Contracts;

public static class ParameterType
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

    public static bool IsArray(string type) => type.EndsWith("[]", StringComparison.Ordinal);
}
