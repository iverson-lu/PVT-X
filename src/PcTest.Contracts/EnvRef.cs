using System.Text.Json;

namespace PcTest.Contracts;

public sealed class EnvRef
{
    public string Env { get; set; } = string.Empty;
    public JsonElement? Default { get; set; }
    public bool Required { get; set; }
    public bool Secret { get; set; }

    public static bool TryParse(JsonElement element, out EnvRef? envRef)
    {
        envRef = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("$env", out var envProperty))
        {
            return false;
        }

        var envName = envProperty.GetString() ?? string.Empty;
        var parsed = new EnvRef { Env = envName };

        if (element.TryGetProperty("default", out var defaultProp))
        {
            parsed.Default = defaultProp;
        }

        if (element.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.True)
        {
            parsed.Required = true;
        }

        if (element.TryGetProperty("secret", out var secretProp) && secretProp.ValueKind == JsonValueKind.True)
        {
            parsed.Secret = true;
        }

        envRef = parsed;
        return true;
    }
}
