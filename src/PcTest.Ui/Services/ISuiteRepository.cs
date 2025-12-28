using System.IO;
using PcTest.Contracts.Manifests;

namespace PcTest.Ui.Services;

/// <summary>
/// Repository for managing test suites.
/// </summary>
public interface ISuiteRepository
{
    /// <summary>
    /// Gets all discovered suites.
    /// </summary>
    Task<IReadOnlyList<SuiteInfo>> GetAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a suite by identity.
    /// </summary>
    Task<SuiteInfo?> GetByIdentityAsync(string identity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new suite.
    /// </summary>
    Task<SuiteInfo> CreateAsync(TestSuiteManifest manifest, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing suite.
    /// </summary>
    Task UpdateAsync(SuiteInfo suite, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a suite.
    /// </summary>
    Task DeleteAsync(string identity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Imports a suite from a file.
    /// </summary>
    Task<SuiteInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Exports a suite to a file.
    /// </summary>
    Task ExportAsync(string identity, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates a suite manifest.
    /// </summary>
    ValidationResult ValidateSuite(TestSuiteManifest manifest);
}

/// <summary>
/// Information about a suite including its manifest and file location.
/// </summary>
public sealed class SuiteInfo
{
    public TestSuiteManifest Manifest { get; set; } = new();
    public string ManifestPath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string Identity => Manifest.Identity;
}

/// <summary>
/// Validation result for manifests.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

