using System.IO;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for managing test plans.
/// </summary>
public sealed class PlanRepository : IPlanRepository
{
    private readonly IDiscoveryService _discoveryService;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;

    public PlanRepository(
        IDiscoveryService discoveryService,
        ISettingsService settingsService,
        IFileSystemService fileSystemService)
    {
        _discoveryService = discoveryService;
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
    }

    public async Task<IReadOnlyList<PlanInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync(cancellationToken);
        }

        return discovery.TestPlans.Values
            .Select(p => new PlanInfo
            {
                Manifest = p.Manifest,
                ManifestPath = p.ManifestPath,
                FolderPath = p.FolderPath
            })
            .ToList();
    }

    public async Task<PlanInfo?> GetByIdentityAsync(string identity, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(p => p.Identity == identity);
    }

    public async Task<PlanInfo> CreateAsync(TestPlanManifest manifest, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        var plansRoot = settings.ResolvedTestPlansRoot;
        
        // Create folder for the plan
        var folderName = manifest.Id;
        var folderPath = Path.Combine(plansRoot, folderName);
        _fileSystemService.CreateDirectory(folderPath);
        
        // Write manifest
        var manifestPath = Path.Combine(folderPath, "plan.manifest.json");
        var json = JsonDefaults.Serialize(manifest);
        await _fileSystemService.WriteAllTextAsync(manifestPath, json, cancellationToken);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
        
        return new PlanInfo
        {
            Manifest = manifest,
            ManifestPath = manifestPath,
            FolderPath = folderPath
        };
    }

    public async Task UpdateAsync(PlanInfo plan, CancellationToken cancellationToken = default)
    {
        var json = JsonDefaults.Serialize(plan.Manifest);
        await _fileSystemService.WriteAllTextAsync(plan.ManifestPath, json, cancellationToken);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
    }

    public async Task DeleteAsync(string identity, CancellationToken cancellationToken = default)
    {
        var plan = await GetByIdentityAsync(identity, cancellationToken);
        if (plan is null) return;
        
        _fileSystemService.DeleteDirectory(plan.FolderPath, recursive: true);
        
        // Refresh discovery
        await _discoveryService.DiscoverAsync(cancellationToken);
    }

    public async Task<PlanInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await _fileSystemService.ReadAllTextAsync(filePath, cancellationToken);
        var manifest = JsonDefaults.Deserialize<TestPlanManifest>(json);
        
        if (manifest is null)
        {
            throw new InvalidOperationException("Failed to parse plan manifest");
        }
        
        return await CreateAsync(manifest, cancellationToken);
    }

    public async Task ExportAsync(string identity, string filePath, CancellationToken cancellationToken = default)
    {
        var plan = await GetByIdentityAsync(identity, cancellationToken);
        if (plan is null)
        {
            throw new InvalidOperationException($"Plan not found: {identity}");
        }
        
        var json = JsonDefaults.Serialize(plan.Manifest);
        await _fileSystemService.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public ValidationResult ValidatePlan(TestPlanManifest manifest)
    {
        var result = new ValidationResult();
        
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            result.IsValid = false;
            result.Errors.Add("Plan ID is required");
        }
        
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            result.IsValid = false;
            result.Errors.Add("Plan name is required");
        }
        
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            result.IsValid = false;
            result.Errors.Add("Plan version is required");
        }
        
        if (manifest.TestSuites.Count == 0)
        {
            result.Warnings.Add("Plan has no suites");
        }
        
        // Validate suite references
        foreach (var suiteNode in manifest.TestSuites)
        {
            if (string.IsNullOrWhiteSpace(suiteNode.NodeId))
            {
                result.IsValid = false;
                result.Errors.Add("Empty suite reference found");
            }
            else if (!suiteNode.NodeId.Contains('@'))
            {
                result.Warnings.Add($"Suite reference '{suiteNode.NodeId}' should include version (e.g., '{suiteNode.NodeId}@1.0.0')");
            }
        }
        
        return result;
    }
}

