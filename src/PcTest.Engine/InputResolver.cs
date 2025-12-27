using System.Collections;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class InputResolver
{
    public ResolvedInputs ResolveInputs(
        TestCaseManifest manifest,
        Dictionary<string, JsonElement>? baseInputs,
        Dictionary<string, JsonElement>? overrideInputs,
        Dictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, param) in manifest.Parameters)
        {
            if (param.Default is { } def)
            {
                merged[name] = def;
            }
        }

        if (baseInputs is not null)
        {
            foreach (var (key, value) in baseInputs)
            {
                merged[key] = value;
            }
        }

        if (overrideInputs is not null)
        {
            foreach (var (key, value) in overrideInputs)
            {
                merged[key] = value;
            }
        }

        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var redaction = new RedactionMetadata();
        foreach (var (name, value) in merged)
        {
            manifest.Parameters.TryGetValue(name, out var paramDef);
            var paramType = paramDef?.Type ?? "string";
            if (IsEnvRef(value))
            {
                var envRef = value.Deserialize<EnvRef>(JsonDefaults.Options);
                if (envRef?.Name is null)
                {
                    resolved[name] = ConvertValue(paramType, value, paramDef);
                    continue;
                }

                if (!effectiveEnvironment.TryGetValue(envRef.Name, out var envValue) || IsEmpty(envValue))
                {
                    if (envRef.Default is { } defaultValue)
                    {
                        resolved[name] = ConvertValue(paramType, defaultValue, paramDef);
                    }
                    else if (envRef.Required)
                    {
                        throw new InvalidOperationException(JsonSerializer.Serialize(new
                        {
                            code = ErrorCodes.EnvRefResolveFailed,
                            payload = new
                            {
                                parameterName = name,
                                nodeId,
                                reason = "Missing"
                            }
                        }, JsonDefaults.Options));
                    }
                    else
                    {
                        resolved[name] = null;
                    }
                }
                else
                {
                    resolved[name] = ConvertEnvValue(paramType, envValue, paramDef);
                }

                if (envRef.Secret)
                {
                    redaction.SecretInputs.Add(name);
                }
            }
            else
            {
                resolved[name] = ConvertValue(paramType, value, paramDef);
            }
        }

        return new ResolvedInputs(resolved, redaction);
    }

    public Dictionary<string, string> ResolveEnvironment(
        Dictionary<string, string>? planEnv,
        Dictionary<string, string>? suiteEnv,
        Dictionary<string, string>? envOverride)
    {
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                effective[key] = value;
            }
        }

        void Merge(Dictionary<string, string>? source)
        {
            if (source is null)
            {
                return;
            }

            foreach (var (key, value) in source)
            {
                effective[key] = value;
            }
        }

        Merge(suiteEnv);
        Merge(planEnv);
        Merge(envOverride);
        return effective;
    }

    public RedactionMetadata ResolveEnvRedaction(Dictionary<string, string> effectiveEnvironment, Dictionary<string, string>? envOverride)
    {
        var redaction = new RedactionMetadata();
        if (envOverride is null)
        {
            return redaction;
        }

        foreach (var key in envOverride.Keys)
        {
            redaction.SecretEnv.Add(key);
        }

        return redaction;
    }

    private static bool IsEmpty(string? value) => value is null || value == "";

    private static bool IsEnvRef(JsonElement value)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$env", out _);

    private static object? ConvertValue(string type, JsonElement value, ParameterDefinition? paramDef)
    {
        return type.ToLowerInvariant() switch
        {
            "int" => value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString() ?? "0"),
            "double" => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.Parse(value.GetString() ?? "0"),
            "boolean" => value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False
                ? value.GetBoolean()
                : bool.Parse(value.GetString() ?? "false"),
            "array" => value.ValueKind == JsonValueKind.Array ? value.Deserialize<object[]>() : Array.Empty<object>(),
            "enum" => ValidateEnum(value, paramDef),
            _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.Deserialize<object>()
        };
    }

    private static object? ConvertEnvValue(string type, string envValue, ParameterDefinition? paramDef)
    {
        return type.ToLowerInvariant() switch
        {
            "int" => int.Parse(envValue),
            "double" => double.Parse(envValue),
            "boolean" => bool.Parse(envValue),
            "array" => JsonDocument.Parse(envValue).RootElement.Deserialize<object[]>(),
            "enum" => ValidateEnum(JsonDocument.Parse(JsonSerializer.Serialize(envValue)).RootElement, paramDef),
            _ => envValue
        };
    }

    private static object? ValidateEnum(JsonElement value, ParameterDefinition? paramDef)
    {
        if (paramDef?.EnumValues is null)
        {
            return value.GetString();
        }

        var allowed = paramDef.EnumValues.Value.ValueKind == JsonValueKind.Array
            ? paramDef.EnumValues.Value.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var literal = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        if (literal is null || !allowed.Contains(literal))
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(new
            {
                code = ErrorCodes.ManifestInvalid,
                payload = new
                {
                    reason = "EnumValue"
                }
            }, JsonDefaults.Options));
        }

        return literal;
    }
}

public sealed record ResolvedInputs(Dictionary<string, object?> Values, RedactionMetadata Redaction);
