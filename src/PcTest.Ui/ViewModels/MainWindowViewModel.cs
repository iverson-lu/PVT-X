using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _currentPageName = "Plan";

    [ObservableProperty]
    private string _appTitle = "PVT-X | PC Validation Platform";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainWindowViewModel(
        INavigationService navigationService,
        ISettingsService settingsService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        
        _navigationService.Navigated += OnNavigated;
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        CurrentPageName = e.PageName;
        StatusMessage = $"Navigated to {e.PageName}";
    }

    [RelayCommand]
    private void NavigateTo(string pageName)
    {
        _navigationService.NavigateTo(pageName);
    }

    public void Initialize()
    {
        // Navigate to default landing page
        var defaultPage = _settingsService.CurrentSettings.DefaultLandingPage;
        _navigationService.NavigateTo(defaultPage);
    }
}
