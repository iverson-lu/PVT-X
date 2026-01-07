using System.Text.Json;

namespace PcTest.Runner;

public sealed class RebootRequest
{
    public const string RequestType = "control.reboot_required";

    public int NextPhase { get; init; }
    public string Reason { get; init; } = string.Empty;
    public int? DelaySec { get; init; }

    public static bool TryLoad(string rebootPath, out RebootRequest? request, out string? error)
    {
        request = null;
        error = null;

        if (!File.Exists(rebootPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(rebootPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "reboot.json root must be an object";
                return true;
            }

            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "type",
                "nextPhase",
                "reason",
                "reboot"
            };

            foreach (var prop in root.EnumerateObject())
            {
                if (!allowedKeys.Contains(prop.Name))
                {
                    error = $"Unexpected property '{prop.Name}' in reboot.json";
                    return true;
                }
            }

            if (!root.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String ||
                !string.Equals(typeProp.GetString(), RequestType, StringComparison.Ordinal))
            {
                error = "reboot.json 'type' must be 'control.reboot_required'";
                return true;
            }

            if (!root.TryGetProperty("nextPhase", out var nextPhaseProp) ||
                nextPhaseProp.ValueKind != JsonValueKind.Number ||
                !nextPhaseProp.TryGetInt32(out var nextPhase) ||
                nextPhase < 1)
            {
                error = "reboot.json 'nextPhase' must be an integer >= 1";
                return true;
            }

            if (!root.TryGetProperty("reason", out var reasonProp) ||
                reasonProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(reasonProp.GetString()))
            {
                error = "reboot.json 'reason' must be a non-empty string";
                return true;
            }

            int? delaySec = null;
            if (root.TryGetProperty("reboot", out var rebootProp))
            {
                if (rebootProp.ValueKind != JsonValueKind.Object)
                {
                    error = "reboot.json 'reboot' must be an object when provided";
                    return true;
                }

                var rebootKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delaySec" };
                foreach (var prop in rebootProp.EnumerateObject())
                {
                    if (!rebootKeys.Contains(prop.Name))
                    {
                        error = $"Unexpected property 'reboot.{prop.Name}' in reboot.json";
                        return true;
                    }
                }

                if (rebootProp.TryGetProperty("delaySec", out var delayProp))
                {
                    if (delayProp.ValueKind != JsonValueKind.Number ||
                        !delayProp.TryGetInt32(out var parsedDelay) ||
                        parsedDelay < 0)
                    {
                        error = "reboot.json 'reboot.delaySec' must be an integer >= 0";
                        return true;
                    }

                    delaySec = parsedDelay;
                }
            }

            request = new RebootRequest
            {
                NextPhase = nextPhase,
                Reason = reasonProp.GetString() ?? string.Empty,
                DelaySec = delaySec
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse reboot.json: {ex.Message}";
            return true;
        }
    }
}
