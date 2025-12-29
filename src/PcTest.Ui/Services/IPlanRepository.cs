using System.IO;
using PcTest.Contracts.Manifests;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for managing test plans.
/// </summary>
public interface IPlanRepository
{
    /// <summary>
    /// Gets all discovered plans.
    /// </summary>
    Task<IReadOnlyList<PlanInfo>> GetAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a plan by identity.
    /// </summary>
    Task<PlanInfo?> GetByIdentityAsync(string identity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new plan.
    /// </summary>
    Task<PlanInfo> CreateAsync(TestPlanManifest manifest, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing plan.
    /// </summary>
    Task UpdateAsync(PlanInfo plan, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a plan.
    /// </summary>
    Task DeleteAsync(string identity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Imports a plan from a file.
    /// </summary>
    Task<PlanInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Exports a plan to a file.
    /// </summary>
    Task ExportAsync(string identity, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates a plan manifest.
    /// </summary>
    ValidationResult ValidatePlan(TestPlanManifest manifest);
}

/// <summary>
/// Information about a plan including its manifest and file location.
/// </summary>
public sealed class PlanInfo
{
    public TestPlanManifest Manifest { get; set; } = new();
    public string ManifestPath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string Identity => Manifest.Identity;
}

