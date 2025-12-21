using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

/// <summary>
/// Validates and converts user-provided parameters to their strongly-typed equivalents.
/// </summary>
public static class ParameterBinder
{
    /// <summary>
    /// Binds raw parameter strings to their defined types using the manifest definitions.
    /// </summary>
    /// <param name="manifest">Manifest containing parameter definitions.</param>
    /// <param name="provided">Raw user-provided parameter values.</param>
    /// <returns>Dictionary of parameter names to bound values.</returns>
    /// <exception cref="InvalidDataException">Thrown when validation fails or unknown parameters are supplied.</exception>
    public static IReadOnlyDictionary<string, BoundParameterValue> Bind(TestManifest manifest, IDictionary<string, string> provided)
    {
        var result = new Dictionary<string, BoundParameterValue>(StringComparer.OrdinalIgnoreCase);
        var definitions = manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var providedKeys = new HashSet<string>(provided.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            var hasInput = TryGetProvided(provided, definition.Name, out var raw);

            if (!hasInput && definition.Required && definition.Default is null)
            {
                throw new InvalidDataException($"Missing required parameter '{definition.Name}'.");
            }

            if (!hasInput && definition.Default is null)
            {
                continue;
            }

            try
            {
                var value = hasInput
                    ? ParameterValidation.ConvertFromInput(definition, raw!)
                    : ParameterValidation.ConvertDefault(definition);

                result[definition.Name] = new BoundParameterValue(definition, value, hasInput || definition.Default is not null);
                providedKeys.RemoveWhere(k => string.Equals(k, definition.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (ValidationException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        if (providedKeys.Count > 0)
        {
            throw new InvalidDataException($"Unknown parameters: {string.Join(", ", providedKeys)}");
        }

        return result;
    }

    private static bool TryGetProvided(IDictionary<string, string> provided, string name, out string? value)
    {
        foreach (var kvp in provided)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
