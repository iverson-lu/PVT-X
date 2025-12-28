using System.Windows.Controls;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Pages;

/// <summary>
/// History page for viewing run history.
/// </summary>
public partial class HistoryPage : Page
{
    private readonly HistoryViewModel _viewModel;

    public HistoryPage(HistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        InitializeComponent();
        
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
}
