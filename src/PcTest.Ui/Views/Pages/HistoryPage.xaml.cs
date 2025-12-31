using System.Linq;
using System.Windows.Controls;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Unified History page (replaces old History + Logs & Results).
/// Provides master-detail layout with run list and run inspector.
/// </summary>
public partial class HistoryPage : Page
{
    private readonly HistoryViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public HistoryPage(HistoryViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Load the runs, optionally with a specific runId from navigation parameter
        var parameter = _navigationService.CurrentParameter;
        
        await _viewModel.LoadAsync();
        
        // If a runId was provided as parameter, select that run
        if (parameter is string runId && !string.IsNullOrEmpty(runId))
        {
            var run = _viewModel.Runs.FirstOrDefault(r => r.RunId == runId);
            if (run != null)
            {
                _viewModel.SelectedRun = run;
            }
        }
    }

    private void ArtifactsTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ArtifactNodeViewModel artifact)
        {
            _viewModel.SelectedArtifact = artifact;
        }
    }
}
