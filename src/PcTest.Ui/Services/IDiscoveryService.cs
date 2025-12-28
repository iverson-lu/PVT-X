using PcTest.Engine.Discovery;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for discovering test assets.
/// </summary>
public interface IDiscoveryService
{
    /// <summary>
    /// Discovers all test cases, suites, and plans.
    /// </summary>
    Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current discovery result.
    /// </summary>
    DiscoveryResult? CurrentDiscovery { get; }
    
    /// <summary>
    /// Event raised when discovery completes.
    /// </summary>
    event EventHandler<DiscoveryResult>? DiscoveryCompleted;
}
