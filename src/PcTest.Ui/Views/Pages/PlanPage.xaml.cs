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
    private int _lastConfirmedTabIndex;

    public PlanPage(PlanViewModel viewModel, INavigationService navigationService)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += PlanPage_Loaded;
        Unloaded += PlanPage_Unloaded;
        
        // Subscribe to navigation events to check for unsaved changes
        _navigationService.Navigating += OnNavigating;
        
        // Initialize the last confirmed tab index
        _lastConfirmedTabIndex = 0;
        
        // Hook up tab click interception
        MainTabControl.PreviewMouseDown += MainTabControl_PreviewMouseDown;
    }

    private void MainTabControl_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Find which tab item was clicked
        var clickedElement = e.OriginalSource as System.Windows.DependencyObject;
        while (clickedElement != null && !(clickedElement is TabItem))
        {
            clickedElement = System.Windows.Media.VisualTreeHelper.GetParent(clickedElement);
        }

        if (clickedElement is TabItem clickedTab)
        {
            var newIndex = MainTabControl.Items.IndexOf(clickedTab);
            
            // If switching to a different tab, check for unsaved changes
            if (newIndex != _lastConfirmedTabIndex && newIndex >= 0)
            {
                if (!_viewModel.CheckUnsavedChanges(newIndex))
                {
                    // User cancelled, prevent the tab switch
                    e.Handled = true;
                    return;
                }
                
                // Tab change allowed, update last confirmed index
                _lastConfirmedTabIndex = newIndex;
            }
        }
    }

    private void OnNavigating(object? sender, NavigatingEventArgs e)
    {
        // Only check if navigating away from Plan page
        if (e.FromPage == "Plan" && e.ToPage != "Plan")
        {
            if (!_viewModel.CheckUnsavedChangesBeforeNavigate())
            {
                e.Cancel = true;
            }
        }
    }

    private void PlanPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Unsubscribe from navigation events
        _navigationService.Navigating -= OnNavigating;
    }

    private async void PlanPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var parameter = _navigationService.CurrentParameter;
        
        // Handle PlanNavigationParameter (from back navigation)
        if (parameter is PlanNavigationParameter planNav)
        {
            _viewModel.SelectedTabIndex = planNav.TabIndex;
            _lastConfirmedTabIndex = planNav.TabIndex;
            await _viewModel.InitializeAsync();
            _viewModel.SelectItemByIdentity(planNav.TabIndex, planNav.TargetIdentity);
        }
        // Handle simple tab index (legacy compatibility)
        else if (parameter is int tabIndex)
        {
            _viewModel.SelectedTabIndex = tabIndex;
            _lastConfirmedTabIndex = tabIndex;
            await _viewModel.InitializeAsync();
        }
        else
        {
            await _viewModel.InitializeAsync();
        }
    }
}
