using System.Windows.Controls;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Plan page for managing test cases, suites, and plans.
/// </summary>
public partial class PlanPage : Page
{
    private readonly PlanViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public PlanPage(PlanViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += PlanPage_Loaded;
    }

    private async void PlanPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var parameter = _navigationService.CurrentParameter;
        
        // Handle PlanNavigationParameter (from back navigation)
        if (parameter is PlanNavigationParameter planNav)
        {
            _viewModel.SelectedTabIndex = planNav.TabIndex;
            await _viewModel.InitializeAsync();
            _viewModel.SelectItemByIdentity(planNav.TabIndex, planNav.TargetIdentity);
        }
        // Handle simple tab index (legacy compatibility)
        else if (parameter is int tabIndex)
        {
            _viewModel.SelectedTabIndex = tabIndex;
            await _viewModel.InitializeAsync();
        }
        else
        {
            await _viewModel.InitializeAsync();
        }
    }
}
