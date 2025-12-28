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

public static class ParameterTypeParser
{
    public static bool TryParse(string? value, out ParameterType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value switch
        {
            "int" => TryAssign(ParameterType.Int, out type),
            "double" => TryAssign(ParameterType.Double, out type),
            "string" => TryAssign(ParameterType.String, out type),
            "boolean" => TryAssign(ParameterType.Boolean, out type),
            "path" => TryAssign(ParameterType.Path, out type),
            "file" => TryAssign(ParameterType.File, out type),
            "folder" => TryAssign(ParameterType.Folder, out type),
            "enum" => TryAssign(ParameterType.Enum, out type),
            "int[]" => TryAssign(ParameterType.IntArray, out type),
            "double[]" => TryAssign(ParameterType.DoubleArray, out type),
            "string[]" => TryAssign(ParameterType.StringArray, out type),
            "boolean[]" => TryAssign(ParameterType.BooleanArray, out type),
            "path[]" => TryAssign(ParameterType.PathArray, out type),
            "file[]" => TryAssign(ParameterType.FileArray, out type),
            "folder[]" => TryAssign(ParameterType.FolderArray, out type),
            "enum[]" => TryAssign(ParameterType.EnumArray, out type),
            _ => false
        };
    }

    private static bool TryAssign(ParameterType parsed, out ParameterType type)
    {
        type = parsed;
        return true;
    }
}
