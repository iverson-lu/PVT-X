using PcTest.Contracts.Manifest;

namespace PcTest.UI.ViewModels;

/// <summary>
/// Presentation model for a discovered test entry.
/// </summary>
public class TestListItemViewModel : ViewModelBase
{
    private string? _description;
    private IReadOnlyList<ParameterDefinition>? _parameters;
    private int? _timeoutSec;
    private IReadOnlyList<string>? _tags;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestListItemViewModel"/> class.
    /// </summary>
    public TestListItemViewModel(DiscoveredTest test)
    {
        Id = test.Id;
        Name = test.Name;
        Version = test.Version;
        Category = test.Category;
        Privilege = test.Privilege;
        ManifestPath = test.ManifestPath;
        TimeoutSec = test.TimeoutSec;
        Tags = test.Tags;
    }

    /// <summary>
    /// Unique identifier of the test.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human readable test name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Version declared in the manifest.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Category for grouping tests.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Required privilege for the test.
    /// </summary>
    public PrivilegePolicy Privilege { get; }

    /// <summary>
    /// Path to the manifest that produced this entry.
    /// </summary>
    public string ManifestPath { get; }

    /// <summary>
    /// Optional timeout for the test in seconds.
    /// </summary>
    public int? TimeoutSec
    {
        get => _timeoutSec;
        set => SetProperty(ref _timeoutSec, value);
    }

    /// <summary>
    /// Optional tags describing the test.
    /// </summary>
    public IReadOnlyList<string>? Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    /// <summary>
    /// Description from the manifest.
    /// </summary>
    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>
    /// Parameter definitions supplied by the manifest.
    /// </summary>
    public IReadOnlyList<ParameterDefinition>? Parameters
    {
        get => _parameters;
        set => SetProperty(ref _parameters, value);
    }
}
