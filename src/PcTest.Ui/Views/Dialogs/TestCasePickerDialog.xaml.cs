using System.Windows;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Dialogs;

/// <summary>
/// Dialog for selecting multiple test cases to add to a suite.
/// </summary>
public partial class TestCasePickerDialog : Wpf.Ui.Controls.FluentWindow
{
    public TestCasePickerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the ViewModel for this dialog.
    /// </summary>
    public TestCasePickerViewModel? ViewModel
    {
        get => DataContext as TestCasePickerViewModel;
        set => DataContext = value;
    }

    /// <summary>
    /// Gets the selected test cases after the dialog is closed.
    /// </summary>
    public IReadOnlyList<SelectableTestCaseViewModel> SelectedTestCases =>
        ViewModel?.GetSelectedTestCases() ?? Array.Empty<SelectableTestCaseViewModel>();

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
