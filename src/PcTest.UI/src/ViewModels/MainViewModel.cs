using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Result;
using PcTest.Contracts.Serialization;
using PcTest.Engine.Discovery;
using PcTest.Engine.Execution;
using PcTest.UI.Infrastructure;

namespace PcTest.UI.ViewModels;

/// <summary>
/// Central view model coordinating discovery, execution, and history for the UI.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly TestExecutor _executor = new();
    private readonly AsyncRelayCommand _discoverCommand;
    private readonly AsyncRelayCommand _runCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _refreshHistoryCommand;
    private CancellationTokenSource? _runCts;
    private TestListItemViewModel? _selectedTest;
    private string _testRoot;
    private string _runsRoot;
    private string? _statusMessage;
    private bool _isRunning;
    private bool _isDiscovering;
    private string? _activeRunFolder;
    private TestResult? _lastResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    public MainViewModel()
    {
        Tests = new ObservableCollection<TestListItemViewModel>();
        Parameters = new ObservableCollection<ParameterInputViewModel>();
        RunHistory = new ObservableCollection<RunHistoryEntryViewModel>();

        _testRoot = ResolveDefaultTestRoot();
        _runsRoot = ResolveDefaultRunsRoot(_testRoot);

        _discoverCommand = new AsyncRelayCommand(DiscoverAsync, () => !IsRunning && !_isDiscovering);
        _runCommand = new AsyncRelayCommand(RunSelectedAsync, () => SelectedTest is not null && !IsRunning);
        _cancelCommand = new RelayCommand(CancelRun, () => IsRunning);
        _refreshHistoryCommand = new AsyncRelayCommand(LoadRunHistoryAsync);
    }

    /// <summary>
    /// Discovered tests.
    /// </summary>
    public ObservableCollection<TestListItemViewModel> Tests { get; }

    /// <summary>
    /// Parameter inputs for the selected test.
    /// </summary>
    public ObservableCollection<ParameterInputViewModel> Parameters { get; }

    /// <summary>
    /// Recent run history entries.
    /// </summary>
    public ObservableCollection<RunHistoryEntryViewModel> RunHistory { get; }

    /// <summary>
    /// Root folder where manifests are discovered.
    /// </summary>
    public string TestRoot
    {
        get => _testRoot;
        set
        {
            if (SetProperty(ref _testRoot, value))
            {
                StatusMessage = "Test root updated.";
            }
        }
    }

    /// <summary>
    /// Root folder where run artifacts are written.
    /// </summary>
    public string RunsRoot
    {
        get => _runsRoot;
        set
        {
            if (SetProperty(ref _runsRoot, value))
            {
                StatusMessage = "Runs root updated.";
            }
        }
    }

    /// <summary>
    /// Currently selected test entry.
    /// </summary>
    public TestListItemViewModel? SelectedTest
    {
        get => _selectedTest;
        set
        {
            if (SetProperty(ref _selectedTest, value))
            {
                _ = LoadSelectedTestDetailsAsync(value);
                UpdateCommandStates();
            }
        }
    }

    /// <summary>
    /// Last status message shown to the user.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Indicates whether a run is in progress.
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                UpdateCommandStates();
            }
        }
    }

    /// <summary>
    /// Path to the most recent run folder.
    /// </summary>
    public string? ActiveRunFolder
    {
        get => _activeRunFolder;
        private set => SetProperty(ref _activeRunFolder, value);
    }

    /// <summary>
    /// Last run result returned by the runner.
    /// </summary>
    public TestResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (SetProperty(ref _lastResult, value))
            {
                OnPropertyChanged(nameof(LastResultStatus));
                OnPropertyChanged(nameof(LastResultMessage));
            }
        }
    }

    /// <summary>
    /// Human-readable representation of the last result.
    /// </summary>
    public string? LastResultStatus => LastResult is null ? null : $"{LastResult.Status} (exit {LastResult.ExitCode?.ToString() ?? "n/a"})";

    /// <summary>
    /// Message from the last run.
    /// </summary>
    public string? LastResultMessage => LastResult?.Message ?? LastResult?.Error?.Message;

    /// <summary>
    /// Command to discover tests.
    /// </summary>
    public AsyncRelayCommand DiscoverCommand => _discoverCommand;

    /// <summary>
    /// Command to run the selected test.
    /// </summary>
    public AsyncRelayCommand RunCommand => _runCommand;

    /// <summary>
    /// Command to cancel an in-flight run.
    /// </summary>
    public RelayCommand CancelCommand => _cancelCommand;

    /// <summary>
    /// Command to refresh run history.
    /// </summary>
    public AsyncRelayCommand RefreshHistoryCommand => _refreshHistoryCommand;

    /// <summary>
    /// Performs initial load of history and discovery.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadRunHistoryAsync();
        await DiscoverAsync();
    }

    private async Task DiscoverAsync()
    {
        try
        {
            _isDiscovering = true;
            UpdateCommandStates();
            StatusMessage = $"Discovering tests under {TestRoot}...";
            var discovered = await Task.Run(() => _executor.Discover(TestRoot).ToList());

            Tests.Clear();
            foreach (var test in discovered)
            {
                Tests.Add(new TestListItemViewModel(test));
            }

            StatusMessage = $"Discovered {Tests.Count} test(s).";

            if (Tests.Count > 0)
            {
                SelectedTest = Tests[0];
            }
            else
            {
                Parameters.Clear();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Discovery failed: {ex.Message}";
            Tests.Clear();
            Parameters.Clear();
        }
        finally
        {
            _isDiscovering = false;
            UpdateCommandStates();
        }
    }

    private async Task LoadSelectedTestDetailsAsync(TestListItemViewModel? test)
    {
        if (test is null)
        {
            Parameters.Clear();
            return;
        }

        try
        {
            StatusMessage = $"Loading manifest for {test.Id}...";
            var manifest = await Task.Run(() => ManifestLoader.Load(test.ManifestPath));
            test.Description = manifest.Description;
            test.Parameters = manifest.Parameters;
            test.TimeoutSec = manifest.TimeoutSec;
            test.Tags = manifest.Tags;

            Parameters.Clear();
            foreach (var parameter in manifest.Parameters ?? Array.Empty<ParameterDefinition>())
            {
                var formattedDefault = FormatDefault(parameter.Default);
                var viewModel = new ParameterInputViewModel(parameter, formattedDefault)
                {
                    Value = formattedDefault ?? string.Empty
                };
                Parameters.Add(viewModel);
            }

            StatusMessage = $"Ready to run {test.Id}.";
        }
        catch (Exception ex)
        {
            Parameters.Clear();
            StatusMessage = $"Failed to load manifest: {ex.Message}";
        }
    }

    private async Task RunSelectedAsync()
    {
        if (SelectedTest is null)
        {
            StatusMessage = "Select a test before running.";
            return;
        }

        var parameterValues = CollectParameters();
        _runCts = new CancellationTokenSource();

        try
        {
            IsRunning = true;
            StatusMessage = $"Running {SelectedTest.Id}...";
            var response = await _executor.RunAsync(TestRoot, SelectedTest.Id, parameterValues, RunsRoot, _runCts.Token);
            ActiveRunFolder = response.RunFolder;
            LastResult = response.Result;
            StatusMessage = $"Run completed: {response.Result.Status}. Artifacts at {response.RunFolder}.";
            await LoadRunHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Run canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsRunning = false;
        }
    }

    private void CancelRun()
    {
        _runCts?.Cancel();
    }

    private async Task LoadRunHistoryAsync()
    {
        try
        {
            var indexPath = Path.Combine(RunsRoot, "index.jsonl");
            if (!File.Exists(indexPath))
            {
                RunHistory.Clear();
                return;
            }

            var lines = await File.ReadAllLinesAsync(indexPath);
            var entries = new List<RunHistoryEntryViewModel>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<RunIndexEntry>(line, JsonDefaults.Options);
                    if (entry is not null)
                    {
                        entries.Add(new RunHistoryEntryViewModel(entry, Path.Combine(RunsRoot, entry.RunId)));
                    }
                }
                catch
                {
                    // Skip malformed entries without crashing the UI.
                }
            }

            RunHistory.Clear();
            foreach (var entry in entries.OrderByDescending(e => e.StartTime))
            {
                RunHistory.Add(entry);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load run history: {ex.Message}";
        }
    }

    private Dictionary<string, string> CollectParameters()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in Parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameter.Value))
            {
                result[parameter.Definition.Name] = parameter.Value;
            }
        }

        return result;
    }

    private static string? FormatDefault(object? defaultValue)
    {
        if (defaultValue is null)
        {
            return null;
        }

        if (defaultValue is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.Array => string.Join(",", element.EnumerateArray().Select(e => e.ToString())),
                _ => element.ToString()
            };
        }

        return defaultValue.ToString();
    }

    private void UpdateCommandStates()
    {
        _discoverCommand.RaiseCanExecuteChanged();
        _runCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private static string ResolveDefaultTestRoot()
    {
        var candidate = FindRepositoryRootContaining("assets", "TestCases");
        if (candidate is not null)
        {
            return Path.Combine(candidate, "assets", "TestCases");
        }

        return Path.Combine(Environment.CurrentDirectory, "assets", "TestCases");
    }

    private static string ResolveDefaultRunsRoot(string testRoot)
    {
        var repoRoot = Directory.GetParent(testRoot)?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            return Path.Combine(repoRoot, "Runs");
        }

        return Path.Combine(Environment.CurrentDirectory, "Runs");
    }

    private static string? FindRepositoryRootContaining(params string[] pathSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var probe = Path.Combine(new[] { current.FullName }.Concat(pathSegments).ToArray());
            if (Directory.Exists(probe))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
