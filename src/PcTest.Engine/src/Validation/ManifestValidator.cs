using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

/// <summary>
/// Performs basic validation for test manifest fields and parameter definitions.
/// </summary>
public static class ManifestValidator
{
    /// <summary>
    /// Validates the provided manifest and throws when required fields are missing or invalid.
    /// </summary>
    /// <param name="manifest">Manifest to validate.</param>
    /// <exception cref="InvalidDataException">Thrown when the manifest content is invalid.</exception>
    public static void Validate(TestManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidDataException("Manifest id is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidDataException("Manifest name is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidDataException("Manifest version is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Category))
        {
            throw new InvalidDataException("Manifest category is required.");
        }

        if (manifest.Parameters is null)
        {
            return;
        }

        var duplicates = manifest.Parameters
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidDataException($"Duplicate parameter names: {string.Join(", ", duplicates)}");
        }

        foreach (var param in manifest.Parameters)
        {
            if (string.IsNullOrWhiteSpace(param.Name))
            {
                throw new InvalidDataException("Parameter name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(param.Type))
            {
                throw new InvalidDataException($"Parameter '{param.Name}' must specify a type.");
            }
        }
    }
}
