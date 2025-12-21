using System.Text.Json;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Serialization;

namespace PcTest.Engine.Discovery;

public static class ManifestLoader
{
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
