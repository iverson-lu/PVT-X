using System.IO;
using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for managing test suites.
/// </summary>
public sealed class SuiteRepository : ISuiteRepository
{
    private readonly IDiscoveryService _discoveryService;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;

    public SuiteRepository(
        IDiscoveryService discoveryService,
        ISettingsService settingsService,
        IFileSystemService fileSystemService)
    {
        _discoveryService = discoveryService;
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
    }

    public async Task<IReadOnlyList<SuiteInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync(cancellationToken);
        }

        return discovery.TestSuites.Values
            .Select(s => new SuiteInfo
            {
                Manifest = s.Manifest,
                ManifestPath = s.ManifestPath,
                FolderPath = s.FolderPath
            })
            .ToList();
    }

    public async Task<SuiteInfo?> GetByIdentityAsync(string identity, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(s => s.Identity == identity);
    }

    public async Task<SuiteInfo> CreateAsync(TestSuiteManifest manifest, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        var suitesRoot = settings.ResolvedTestSuitesRoot;
        
        // Create folder for the suite
        var folderName = $"{manifest.Id}@{manifest.Version}";
        var folderPath = Path.Combine(suitesRoot, folderName);
        _fileSystemService.CreateDirectory(folderPath);
        
        // Write manifest
        var manifestPath = Path.Combine(folderPath, "suite.manifest.json");
        var json = JsonDefaults.Serialize(manifest);
        await _fileSystemService.WriteAllTextAsync(manifestPath, json, cancellationToken);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
        
        return new SuiteInfo
        {
            Manifest = manifest,
            ManifestPath = manifestPath,
            FolderPath = folderPath
        };
    }

    public async Task UpdateAsync(SuiteInfo suite, CancellationToken cancellationToken = default)
    {
        var json = JsonDefaults.Serialize(suite.Manifest);
        await _fileSystemService.WriteAllTextAsync(suite.ManifestPath, json, cancellationToken);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
    }

    public async Task DeleteAsync(string identity, CancellationToken cancellationToken = default)
    {
        var suite = await GetByIdentityAsync(identity, cancellationToken);
        if (suite is null) return;
        
        _fileSystemService.DeleteDirectory(suite.FolderPath, recursive: true);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
    }

    public async Task<SuiteInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await _fileSystemService.ReadAllTextAsync(filePath, cancellationToken);
        var manifest = JsonDefaults.Deserialize<TestSuiteManifest>(json);
        
        if (manifest is null)
        {
            throw new InvalidOperationException("Failed to parse suite manifest");
        }
        
        return await CreateAsync(manifest, cancellationToken);
    }

    public async Task ExportAsync(string identity, string filePath, CancellationToken cancellationToken = default)
    {
        var suite = await GetByIdentityAsync(identity, cancellationToken);
        if (suite is null)
        {
            throw new InvalidOperationException($"Suite not found: {identity}");
        }
        
        var json = JsonDefaults.Serialize(suite.Manifest);
        await _fileSystemService.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public ValidationResult ValidateSuite(TestSuiteManifest manifest)
    {
        var result = new ValidationResult();
        
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            result.IsValid = false;
            result.Errors.Add("Suite ID is required");
        }
        
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            result.IsValid = false;
            result.Errors.Add("Suite name is required");
        }
        
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            result.IsValid = false;
            result.Errors.Add("Suite version is required");
        }
        
        if (manifest.TestCases.Count == 0)
        {
            result.Warnings.Add("Suite has no test cases");
        }
        
        // Validate each node
        var nodeIds = new HashSet<string>();
        foreach (var node in manifest.TestCases)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                result.IsValid = false;
                result.Errors.Add("Node ID is required for all test case nodes");
            }
            else if (!nodeIds.Add(node.NodeId))
            {
                result.IsValid = false;
                result.Errors.Add($"Duplicate node ID: {node.NodeId}");
            }
            
            if (string.IsNullOrWhiteSpace(node.Ref))
            {
                result.IsValid = false;
                result.Errors.Add($"Node '{node.NodeId}' must reference a test case");
            }
        }
        
        return result;
    }
}

