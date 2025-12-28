using System.Text.Json;

namespace PcTest.Contracts;

public sealed record EnvRef(string Env, JsonElement? Default, bool Required, bool Secret)
{
    public static bool TryParse(JsonElement element, out EnvRef? envRef)
    {
        envRef = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? env = null;
        bool required = false;
        bool secret = false;
        JsonElement? @default = null;

        foreach (var prop in element.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "$env":
                    env = prop.Value.GetString();
                    break;
                case "required":
                    required = prop.Value.ValueKind == JsonValueKind.True || (prop.Value.ValueKind == JsonValueKind.False ? false : prop.Value.GetBoolean());
                    break;
                case "secret":
                    secret = prop.Value.ValueKind == JsonValueKind.True || (prop.Value.ValueKind == JsonValueKind.False ? false : prop.Value.GetBoolean());
                    break;
                case "default":
                    @default = prop.Value.Clone();
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(env))
        {
            return false;
        }

        envRef = new EnvRef(env, @default, required, secret);
        return true;
    }
}
