namespace PcTest.Contracts;

public enum ParameterType
{
    Int,
    Double,
    String,
    Boolean,
    Path,
    File,
    Folder,
    Enum,
    IntArray,
    DoubleArray,
    StringArray,
    BooleanArray,
    PathArray,
    FileArray,
    FolderArray,
    EnumArray
}

public static class ParameterTypeHelper
{
    private static readonly Dictionary<string, ParameterType> Map = new(StringComparer.Ordinal)
    {
        ["int"] = ParameterType.Int,
        ["double"] = ParameterType.Double,
        ["string"] = ParameterType.String,
        ["boolean"] = ParameterType.Boolean,
        ["path"] = ParameterType.Path,
        ["file"] = ParameterType.File,
        ["folder"] = ParameterType.Folder,
        ["enum"] = ParameterType.Enum,
        ["int[]"] = ParameterType.IntArray,
        ["double[]"] = ParameterType.DoubleArray,
        ["string[]"] = ParameterType.StringArray,
        ["boolean[]"] = ParameterType.BooleanArray,
        ["path[]"] = ParameterType.PathArray,
        ["file[]"] = ParameterType.FileArray,
        ["folder[]"] = ParameterType.FolderArray,
        ["enum[]"] = ParameterType.EnumArray
    };

    public static bool TryParse(string? value, out ParameterType type)
    {
        if (value is null)
        {
            type = default;
            return false;
        }

        return Map.TryGetValue(value, out type);
    }

    public static bool IsArray(ParameterType type)
        => type is ParameterType.IntArray or ParameterType.DoubleArray or ParameterType.StringArray or ParameterType.BooleanArray or
            ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray or ParameterType.EnumArray;

    public static bool IsEnum(ParameterType type)
        => type is ParameterType.Enum or ParameterType.EnumArray;

    public static bool IsPath(ParameterType type)
        => type is ParameterType.Path or ParameterType.File or ParameterType.Folder or ParameterType.PathArray or
            ParameterType.FileArray or ParameterType.FolderArray;
}
