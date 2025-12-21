using PcTest.Contracts.Manifest;
using PcTest.Engine.Validation;

namespace PcTest.Engine.Discovery;

/// <summary>
/// Discovers available tests under a given root folder.
/// </summary>
public class TestDiscoveryService
{
    /// <summary>
    /// Enumerates discovered tests beneath the provided root directory.
    /// </summary>
    /// <param name="root">Root directory to search for manifest files.</param>
    /// <returns>A sequence of discovered tests.</returns>
    public IEnumerable<DiscoveredTest> Discover(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root must be provided", nameof(root));
        }

        var resolvedRoot = Path.GetFullPath(root);
        if (!Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException($"Test root not found: {resolvedRoot}");
        }

        foreach (var manifestPath in Directory.EnumerateFiles(resolvedRoot, "test.manifest.json", SearchOption.AllDirectories))
        {
            var scriptPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "run.ps1");
            if (!File.Exists(scriptPath))
            {
                continue;
            }

            TestManifest manifest;
            try
            {
                manifest = ManifestLoader.Load(manifestPath);
                ManifestValidator.Validate(manifest);
            }
            catch
            {
                continue;
            }

            yield return new DiscoveredTest(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                manifest.Category,
                manifestPath,
                manifest.Privilege,
                manifest.TimeoutSec,
                manifest.Tags);
        }
    }
}
