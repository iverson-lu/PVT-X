using System.Windows;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Dialogs;

/// <summary>
/// Dialog for purging history runs.
/// </summary>
public partial class PurgeHistoryDialog : Wpf.Ui.Controls.FluentWindow
{
    public PurgeHistoryDialog(PurgeHistoryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => ViewModel.PreviewCommand.Execute(null);
    }

    public PurgeHistoryViewModel ViewModel
    {
        get => (PurgeHistoryViewModel)DataContext;
        private set => DataContext = value;
    }

    private void OnCloseRequested(bool? result)
    {
        DialogResult = result;
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        ViewModel.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }
}
