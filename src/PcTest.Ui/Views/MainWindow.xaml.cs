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
    private Wpf.Ui.Controls.Button? _currentSelectedButton;

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
        _navigationService.Navigated += OnNavigated;
        
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

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        // Update selected state for navigation buttons
        UpdateSelectedNavButton(e.PageName);
    }

    private void UpdateSelectedNavButton(string pageName)
    {
        // Clear previous selection
        if (_currentSelectedButton != null)
        {
            _currentSelectedButton.Style = (Style)FindResource("CompactNavButtonStyle");
        }

        // Set new selection based on page name
        _currentSelectedButton = pageName switch
        {
            "Plan" => PlanNavButton,
            "Run" => RunNavButton,
            "History" => RunsNavButton,
            "Runs" => RunsNavButton,  // Backward compatibility
            "LogsResults" => RunsNavButton,  // Backward compatibility
            "Settings" => SettingsNavButton,
            _ => PlanNavButton
        };

        // Apply selected style
        if (_currentSelectedButton != null)
        {
            _currentSelectedButton.Style = (Style)FindResource("CompactNavButtonSelectedStyle");
        }
    }
}
