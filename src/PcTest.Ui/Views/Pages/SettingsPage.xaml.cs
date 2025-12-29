using System.Windows.Controls;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// Settings page for application configuration.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.Load();
    }
}
