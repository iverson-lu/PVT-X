using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PcTest.Contracts;

public static class Validation
{
    private static readonly Regex IdentityRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static Identity ParseIdentity(string value)
    {
        var trimmed = value.Trim();
        if (trimmed != value)
        {
            throw new InvalidOperationException("Identity contains leading/trailing whitespace.");
        }
        if (trimmed.Count(c => c == '@') != 1)
        {
            throw new InvalidOperationException("Identity must contain exactly one '@'.");
        }
        if (trimmed.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("Identity must not contain whitespace.");
        }
        var parts = trimmed.Split('@');
        var id = parts[0];
        var version = parts[1];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("Identity id and version must be non-empty.");
        }
        if (!IdentityRegex.IsMatch(id))
        {
            throw new InvalidOperationException("Identity id contains invalid characters.");
        }
        return new Identity(id, version);
    }

    public static bool IsEnvRef(JsonNode? node, out EnvRef envRef)
    {
        envRef = new EnvRef();
        if (node is not JsonObject obj)
        {
            return false;
        }
        if (!obj.TryGetPropertyValue("$env", out var envNode))
        {
            return false;
        }
        var env = envNode?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(env))
        {
            throw new InvalidOperationException("EnvRef $env must be non-empty.");
        }
        envRef.Env = env;
        if (obj.TryGetPropertyValue("default", out var defaultNode))
        {
            envRef.Default = defaultNode;
        }
        if (obj.TryGetPropertyValue("required", out var requiredNode))
        {
            envRef.Required = requiredNode?.GetValue<bool>() ?? false;
        }
        if (obj.TryGetPropertyValue("secret", out var secretNode))
        {
            envRef.Secret = secretNode?.GetValue<bool>() ?? false;
        }
        return true;
    }

    public static object? ConvertValue(string type, JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        return type switch
        {
            "int" => value.GetValue<int>(),
            "double" => value.GetValue<double>(),
            "string" => value.GetValue<string>(),
            "boolean" => value.GetValue<bool>(),
            "path" or "file" or "folder" => value.GetValue<string>(),
            "enum" => value.GetValue<string>(),
            "int[]" => value.AsArray().Select(v => v!.GetValue<int>()).ToArray(),
            "double[]" => value.AsArray().Select(v => v!.GetValue<double>()).ToArray(),
            "string[]" or "path[]" or "file[]" or "folder[]" => value.AsArray().Select(v => v!.GetValue<string>()).ToArray(),
            "boolean[]" => value.AsArray().Select(v => v!.GetValue<bool>()).ToArray(),
            "enum[]" => value.AsArray().Select(v => v!.GetValue<string>()).ToArray(),
            _ => value.ToJsonString()
        };
    }

    public static object? ConvertEnvValue(string type, string envValue)
    {
        return type switch
        {
            "string" => envValue,
            "path" or "file" or "folder" => envValue,
            "int" => int.Parse(envValue, CultureInfo.InvariantCulture),
            "double" => double.Parse(envValue, CultureInfo.InvariantCulture),
            "boolean" => ParseBool(envValue),
            "enum" => envValue,
            "string[]" or "path[]" or "file[]" or "folder[]" => JsonSerializer.Deserialize<string[]>(envValue) ?? Array.Empty<string>(),
            "int[]" => JsonSerializer.Deserialize<int[]>(envValue) ?? Array.Empty<int>(),
            "double[]" => JsonSerializer.Deserialize<double[]>(envValue) ?? Array.Empty<double>(),
            "boolean[]" => JsonSerializer.Deserialize<bool[]>(envValue) ?? Array.Empty<bool>(),
            "enum[]" => JsonSerializer.Deserialize<string[]>(envValue) ?? Array.Empty<string>(),
            _ => envValue
        };
    }

    public static bool ParseBool(string value)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (value == "1")
        {
            return true;
        }
        if (value == "0")
        {
            return false;
        }
        throw new InvalidOperationException($"Invalid boolean value '{value}'.");
    }
}
