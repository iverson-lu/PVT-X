using System.Windows.Controls;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Plan page for managing test cases, suites, and plans.
/// </summary>
public partial class PlanPage : Page
{
    private readonly PlanViewModel _viewModel;

    public PlanPage(PlanViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += PlanPage_Loaded;
    }

    private async void PlanPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}
