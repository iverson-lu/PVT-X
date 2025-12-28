using System.Windows.Controls;
using System.Windows.Input;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Logs and Results page for viewing run artifacts and events.
/// </summary>
public partial class LogsResultsPage : Page
{
    private readonly LogsResultsViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public LogsResultsPage(LogsResultsViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += LogsResultsPage_Loaded;
    }

    private async void LogsResultsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var runId = _navigationService.CurrentParameter as string;
        if (!string.IsNullOrEmpty(runId))
        {
            await _viewModel.LoadRunAsync(runId);
        }
        else
        {
            await _viewModel.RunPicker.LoadRecentRunsAsync();
        }
    }

    private async void RunsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.RunPicker.SelectedRun != null)
        {
            await _viewModel.LoadRunAsync(_viewModel.RunPicker.SelectedRun.RunId);
        }
    }
}
