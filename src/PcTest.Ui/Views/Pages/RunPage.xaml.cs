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
    }

    private void RunPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.Initialize(_navigationService.CurrentParameter);
    }
}
