using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class InputResolver
{
    public ResolvedInputs Resolve(TestCaseManifest manifest, Dictionary<string, InputValue>? baseInputs, Dictionary<string, string> environment, string? nodeId)
    {
        var parameterDefinitions = manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var parametersByName = parameterDefinitions.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
        var resolvedInputs = new Dictionary<string, object>(StringComparer.Ordinal);
        var secretInputs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var parameter in parameterDefinitions)
        {
            if (parameter.Default.HasValue)
            {
                resolvedInputs[parameter.Name] = ConvertLiteral(parameter, parameter.Default.Value);
            }
        }

        if (baseInputs is not null)
        {
            foreach (var input in baseInputs)
            {
                if (!parametersByName.TryGetValue(input.Key, out var definition))
                {
                    throw new ValidationException("Input.Unknown", new Dictionary<string, object>
                    {
                        ["input"] = input.Key,
                        ["nodeId"] = nodeId ?? string.Empty
                    });
                }

                var resolved = ResolveInputValue(definition, input.Value, environment, nodeId);
                if (resolved.HasValue)
                {
                    resolvedInputs[input.Key] = resolved.Value;
                }

                if (resolved.SecretValues.Count > 0)
                {
                    secretInputs[input.Key] = resolved.SecretValues;
                }
            }
        }

        foreach (var parameter in parameterDefinitions)
        {
            if (parameter.Required && !resolvedInputs.ContainsKey(parameter.Name))
            {
                throw new ValidationException("Input.RequiredMissing", new Dictionary<string, object>
                {
                    ["input"] = parameter.Name,
                    ["nodeId"] = nodeId ?? string.Empty
                });
            }
        }

        return new ResolvedInputs(resolvedInputs, secretInputs, parametersByName);
    }

    private static ResolvedValue ResolveInputValue(ParameterDefinition definition, InputValue inputValue, Dictionary<string, string> environment, string? nodeId)
    {
        if (inputValue.EnvRef is null)
        {
            if (!inputValue.Literal.HasValue)
            {
                throw new ValidationException("Input.MissingValue", new Dictionary<string, object>
                {
                    ["input"] = definition.Name,
                    ["nodeId"] = nodeId ?? string.Empty
                });
            }

            return new ResolvedValue(ConvertLiteral(definition, inputValue.Literal.Value), Array.Empty<string>(), true);
        }

        var envRef = inputValue.EnvRef;
        if (string.IsNullOrWhiteSpace(envRef.Env))
        {
            throw new ValidationException("EnvRef.Invalid", new Dictionary<string, object>
            {
                ["input"] = definition.Name,
                ["nodeId"] = nodeId ?? string.Empty
            });
        }

        environment.TryGetValue(envRef.Env, out var envValue);
        if (string.IsNullOrEmpty(envValue))
        {
            if (envRef.Default.HasValue)
            {
                var convertedDefault = ConvertLiteral(definition, envRef.Default.Value);
                return new ResolvedValue(convertedDefault, envRef.Secret ? new[] { convertedDefault.ToString() ?? string.Empty } : Array.Empty<string>(), true);
            }

            if (envRef.Required)
            {
                throw new ValidationException("EnvRef.ResolveFailed", new Dictionary<string, object>
                {
                    ["input"] = definition.Name,
                    ["nodeId"] = nodeId ?? string.Empty
                });
            }

            return new ResolvedValue(string.Empty, envRef.Secret ? new[] { string.Empty } : Array.Empty<string>(), false);
        }

        var resolved = ConvertFromString(definition, envValue);
        var secrets = envRef.Secret ? new[] { envValue } : Array.Empty<string>();
        return new ResolvedValue(resolved, secrets, true);
    }

    private static object ConvertLiteral(ParameterDefinition definition, JsonElement element)
    {
        if (!ParameterTypeHelper.TryParse(definition.Type, out var type))
        {
            return element.ToString();
        }

        return type switch
        {
            ParameterType.Int => element.GetInt32(),
            ParameterType.Double => element.GetDouble(),
            ParameterType.Boolean => element.GetBoolean(),
            ParameterType.String or ParameterType.Path or ParameterType.File or ParameterType.Folder => element.GetString() ?? string.Empty,
            ParameterType.Enum => ValidateEnum(definition, element.GetString() ?? string.Empty),
            ParameterType.StringArray or ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray => element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            ParameterType.IntArray => element.EnumerateArray().Select(item => item.GetInt32()).ToArray(),
            ParameterType.DoubleArray => element.EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            ParameterType.BooleanArray => element.EnumerateArray().Select(item => item.GetBoolean()).ToArray(),
            ParameterType.EnumArray => element.EnumerateArray().Select(item => ValidateEnum(definition, item.GetString() ?? string.Empty)).ToArray(),
            _ => element.ToString()
        };
    }

    private static object ConvertFromString(ParameterDefinition definition, string value)
    {
        if (!ParameterTypeHelper.TryParse(definition.Type, out var type))
        {
            return value;
        }

        return type switch
        {
            ParameterType.Int => int.Parse(value, CultureInfo.InvariantCulture),
            ParameterType.Double => double.Parse(value, CultureInfo.InvariantCulture),
            ParameterType.Boolean => ParseBoolean(value),
            ParameterType.String or ParameterType.Path or ParameterType.File or ParameterType.Folder => value,
            ParameterType.Enum => ValidateEnum(definition, value),
            ParameterType.StringArray or ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray => ParseArray(value).EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            ParameterType.IntArray => ParseArray(value).EnumerateArray().Select(item => item.GetInt32()).ToArray(),
            ParameterType.DoubleArray => ParseArray(value).EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            ParameterType.BooleanArray => ParseArray(value).EnumerateArray().Select(item => item.GetBoolean()).ToArray(),
            ParameterType.EnumArray => ParseArray(value).EnumerateArray().Select(item => ValidateEnum(definition, item.GetString() ?? string.Empty)).ToArray(),
            _ => value
        };
    }

    private static JsonElement ParseArray(string value)
    {
        using var doc = JsonDocument.Parse(value);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException("EnvRef.ResolveFailed", new Dictionary<string, object>
            {
                ["reason"] = "ArrayExpected"
            });
        }

        return doc.RootElement.Clone();
    }

    private static bool ParseBoolean(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (value == "1")
        {
            return true;
        }

        if (value == "0")
        {
            return false;
        }

        throw new ValidationException("EnvRef.ResolveFailed", new Dictionary<string, object>
        {
            ["reason"] = "BooleanParse"
        });
    }

    private static string ValidateEnum(ParameterDefinition definition, string value)
    {
        if (definition.EnumValues is null || definition.EnumValues.Length == 0)
        {
            return value;
        }

        if (!definition.EnumValues.Contains(value, StringComparer.Ordinal))
        {
            throw new ValidationException("EnvRef.ResolveFailed", new Dictionary<string, object>
            {
                ["reason"] = "EnumValueInvalid",
                ["value"] = value
            });
        }

        return value;
    }
}

public sealed record ResolvedInputs(
    Dictionary<string, object> Values,
    Dictionary<string, IReadOnlyList<string>> SecretInputs,
    Dictionary<string, ParameterDefinition> ParameterDefinitions);

public sealed record ResolvedValue(object Value, IReadOnlyList<string> SecretValues, bool HasValue);

public sealed class ValidationException : Exception
{
    public ValidationException(string code, Dictionary<string, object> payload)
        : base(code)
    {
        Code = code;
        Payload = payload;
    }

    public string Code { get; }

    public Dictionary<string, object> Payload { get; }
}
