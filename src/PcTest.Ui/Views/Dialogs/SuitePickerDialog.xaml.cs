using System.Windows;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Dialogs;

/// <summary>
/// Dialog for selecting multiple test suites to add to a plan.
/// </summary>
public partial class SuitePickerDialog : Wpf.Ui.Controls.FluentWindow
{
    public SuitePickerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the ViewModel for this dialog.
    /// </summary>
    public SuitePickerViewModel? ViewModel
    {
        get => DataContext as SuitePickerViewModel;
        set => DataContext = value;
    }

    /// <summary>
    /// Gets the selected test suites after the dialog is closed.
    /// </summary>
    public IReadOnlyList<SelectableSuiteViewModel> SelectedSuites =>
        ViewModel?.GetSelectedSuites() ?? Array.Empty<SelectableSuiteViewModel>();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
