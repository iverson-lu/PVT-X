using PcTest.Engine;
using PcTest.Engine.Discovery;

namespace PcTest.Ui.Services;

/// <summary>
/// Adapter for the engine's discovery service.
/// </summary>
public sealed class DiscoveryServiceAdapter : IDiscoveryService
{
    private readonly TestEngine _engine;
    private readonly ISettingsService _settingsService;
    private DiscoveryResult? _currentDiscovery;

    public event EventHandler<DiscoveryResult>? DiscoveryCompleted;

    public DiscoveryServiceAdapter(TestEngine engine, ISettingsService settingsService)
    {
        _engine = engine;
        _settingsService = settingsService;
    }

    public DiscoveryResult? CurrentDiscovery => _currentDiscovery;

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var settings = _settingsService.CurrentSettings;
            
            _engine.Configure(
                settings.ResolvedTestCasesRoot,
                settings.ResolvedTestSuitesRoot,
                settings.ResolvedTestPlansRoot,
                settings.ResolvedRunsRoot,
                settings.ResolvedAssetsRoot);
            
            _currentDiscovery = _engine.Discover();
            
            DiscoveryCompleted?.Invoke(this, _currentDiscovery);
            
            return _currentDiscovery;
        }, cancellationToken);
    }
}
