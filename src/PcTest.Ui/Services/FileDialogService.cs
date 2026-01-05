using System.Windows;
using Microsoft.Win32;

namespace PcTest.Ui.Services;

/// <summary>
/// Implementation of file dialog service.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = initialDirectory ?? Environment.CurrentDirectory
        };
        
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string title, string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName ?? string.Empty,
            InitialDirectory = initialDirectory ?? Environment.CurrentDirectory
        };
        
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowFolderBrowserDialog(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory ?? Environment.CurrentDirectory
        };
        
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool ShowConfirmation(string title, string message)
    {
        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public bool? ShowYesNoCancel(string title, string message)
    {
        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        
        return result switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null
        };
    }

    public IReadOnlyList<(string Id, string Name, string Version, string FolderName)> ShowTestCasePicker(
        PcTest.Engine.Discovery.DiscoveryResult discovery,
        IEnumerable<string>? excludeRefs = null)
    {
        var viewModel = new ViewModels.TestCasePickerViewModel();
        viewModel.LoadTestCases(discovery, excludeRefs);

        var dialog = new Views.Dialogs.TestCasePickerDialog
        {
            Owner = Application.Current.MainWindow,
            ViewModel = viewModel
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.SelectedTestCases
                .Select(tc => (tc.Id, tc.Name, tc.Version, tc.FolderName))
                .ToList();
        }

        return Array.Empty<(string, string, string, string)>();
    }

    public IReadOnlyList<string> ShowSuitePicker(
        IEnumerable<ViewModels.SuiteListItemViewModel> suites,
        IEnumerable<string>? excludeIdentities = null)
    {
        var viewModel = new ViewModels.SuitePickerViewModel();
        viewModel.LoadSuites(suites, excludeIdentities);

        var dialog = new Views.Dialogs.SuitePickerDialog
        {
            Owner = Application.Current.MainWindow,
            ViewModel = viewModel
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.SelectedSuites
                .Select(s => s.Identity)
                .ToList();
        }

        return Array.Empty<string>();
    }

    public bool ShowPurgeHistoryDialog(ViewModels.PurgeHistoryViewModel viewModel)
    {
        var dialog = new Views.Dialogs.PurgeHistoryDialog(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true;
    }
}
