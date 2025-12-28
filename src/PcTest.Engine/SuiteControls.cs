using System.Text.Json;

namespace PcTest.Engine;

public sealed record SuiteControls
{
    public int Repeat { get; init; } = 1;
    public int MaxParallel { get; init; } = 1;
    public bool ContinueOnFailure { get; init; }
    public int RetryOnError { get; init; }
    public string TimeoutPolicy { get; init; } = "AbortOnTimeout";

    public static SuiteControls FromJson(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Undefined || element.Value.ValueKind == JsonValueKind.Null)
        {
            return new SuiteControls();
        }

        if (element.Value.ValueKind != JsonValueKind.Object)
        {
            return new SuiteControls();
        }

        var controls = new SuiteControls();
        foreach (var property in element.Value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "repeat":
                    controls = controls with { Repeat = property.Value.GetInt32() };
                    break;
                case "maxParallel":
                    controls = controls with { MaxParallel = property.Value.GetInt32() };
                    break;
                case "continueOnFailure":
                    controls = controls with { ContinueOnFailure = property.Value.GetBoolean() };
                    break;
                case "retryOnError":
                    controls = controls with { RetryOnError = property.Value.GetInt32() };
                    break;
                case "timeoutPolicy":
                    controls = controls with { TimeoutPolicy = property.Value.GetString() ?? "AbortOnTimeout" };
                    break;
            }
        }

        return controls;
    }
}
