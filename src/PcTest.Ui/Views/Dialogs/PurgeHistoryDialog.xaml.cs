using System.Windows;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Dialogs;

/// <summary>
/// Dialog for purging history.
/// </summary>
public partial class PurgeHistoryDialog : Wpf.Ui.Controls.FluentWindow
{
    public PurgeHistoryDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the ViewModel for this dialog.
    /// </summary>
    public PurgeHistoryViewModel? ViewModel
    {
        get => DataContext as PurgeHistoryViewModel;
        set => DataContext = value;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var didPurge = await ViewModel.PurgeAsync();
        if (didPurge)
        {
            DialogResult = true;
            Close();
        }
    }
}
