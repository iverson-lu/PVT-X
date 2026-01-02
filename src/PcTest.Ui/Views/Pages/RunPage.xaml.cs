using System.ComponentModel;
using System.Windows.Controls;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Run page for executing tests.
/// </summary>
public partial class RunPage : Page
{
    private readonly RunViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public RunPage(RunViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += RunPage_Loaded;
        Unloaded += RunPage_Unloaded;
        
        // Subscribe to ConsoleOutput property changes for auto-scroll
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void RunPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.Initialize(_navigationService.CurrentParameter);
    }
    
    private void RunPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunViewModel.ConsoleOutput))
        {
            // Auto-scroll to bottom when new console output is added
            ConsoleScrollViewer?.ScrollToEnd();
        }
    }
}
