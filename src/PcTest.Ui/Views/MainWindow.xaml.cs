using System.Windows;
using System.Windows.Controls;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Wpf.Ui.Controls;

namespace PcTest.Ui.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        
        DataContext = viewModel;
        
        InitializeComponent();
        
        // Set up navigation
        _navigationService.SetFrame(ContentFrame);
        
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
        
        // Navigate to default page
        _navigationService.NavigateTo("Plan");
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && button.Tag is string pageName)
        {
            _navigationService.NavigateTo(pageName);
        }
    }
}
