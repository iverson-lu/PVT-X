using System.Text.Json.Serialization;

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

public static class ParameterTypeExtensions
{
    public static ParameterType Parse(string value) => value switch
    {
        "int" => ParameterType.Int,
        "double" => ParameterType.Double,
        "string" => ParameterType.String,
        "boolean" => ParameterType.Boolean,
        "path" => ParameterType.Path,
        "file" => ParameterType.File,
        "folder" => ParameterType.Folder,
        "enum" => ParameterType.Enum,
        "int[]" => ParameterType.IntArray,
        "double[]" => ParameterType.DoubleArray,
        "string[]" => ParameterType.StringArray,
        "boolean[]" => ParameterType.BooleanArray,
        "path[]" => ParameterType.PathArray,
        "file[]" => ParameterType.FileArray,
        "folder[]" => ParameterType.FolderArray,
        "enum[]" => ParameterType.EnumArray,
        _ => throw new InvalidOperationException($"Unsupported parameter type '{value}'.")
    };

    public static bool IsArray(this ParameterType type) => type is
        ParameterType.IntArray or ParameterType.DoubleArray or ParameterType.StringArray or
        ParameterType.BooleanArray or ParameterType.PathArray or ParameterType.FileArray or
        ParameterType.FolderArray or ParameterType.EnumArray;

    public static string ToSchemaString(this ParameterType type) => type switch
    {
        ParameterType.Int => "int",
        ParameterType.Double => "double",
        ParameterType.String => "string",
        ParameterType.Boolean => "boolean",
        ParameterType.Path => "path",
        ParameterType.File => "file",
        ParameterType.Folder => "folder",
        ParameterType.Enum => "enum",
        ParameterType.IntArray => "int[]",
        ParameterType.DoubleArray => "double[]",
        ParameterType.StringArray => "string[]",
        ParameterType.BooleanArray => "boolean[]",
        ParameterType.PathArray => "path[]",
        ParameterType.FileArray => "file[]",
        ParameterType.FolderArray => "folder[]",
        ParameterType.EnumArray => "enum[]",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}

public sealed record ParameterDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    [JsonPropertyName("default")]
    public object? Default { get; init; }

    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("enumValues")]
    public string[]? EnumValues { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("uiHint")]
    public string? UiHint { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("help")]
    public string? Help { get; init; }

    public ParameterType ParsedType => ParameterTypeExtensions.Parse(Type);
}
