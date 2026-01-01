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

        await _viewModel.InitializeAsync(parameter);
    }

    private void ArtifactsTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ArtifactNodeViewModel artifact)
        {
            _viewModel.SelectedArtifact = artifact;
        }
    }
}
