using System.Text.Json;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Serialization;

namespace PcTest.Engine.Discovery;

/// <summary>
/// Loads and deserializes test manifest files.
/// </summary>
public static class ManifestLoader
{
    /// <summary>
    /// Reads and parses a manifest from the provided file path.
    /// </summary>
    /// <param name="manifestPath">Path to the manifest JSON file.</param>
    /// <returns>The deserialized manifest object.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the manifest file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the manifest cannot be deserialized.</exception>
    public static TestManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Manifest not found.", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<TestManifest>(json, JsonDefaults.Options)
                       ?? throw new InvalidDataException("Manifest could not be deserialized.");

        return manifest;
    }
}
