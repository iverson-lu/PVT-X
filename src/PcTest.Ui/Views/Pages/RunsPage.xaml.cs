using System.Windows.Controls;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Unified Runs page (replaces History + Logs & Results).
/// Provides master-detail layout with run list and run inspector.
/// </summary>
public partial class RunsPage : Page
{
    private readonly RunsViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public RunsPage(RunsViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Initialize with any navigation parameter (e.g., specific runId)
        await _viewModel.InitializeAsync(_navigationService.CurrentParameter);
    }

    private void ArtifactsTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ArtifactNodeViewModel artifact)
        {
            _viewModel.SelectedArtifact = artifact;
        }
    }
}
