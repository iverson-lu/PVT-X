using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for displaying file dialogs (abstracted for testability).
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows an open file dialog.
    /// </summary>
    string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null);
    
    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    string? ShowSaveFileDialog(string title, string filter, string? defaultFileName = null, string? initialDirectory = null);
    
    /// <summary>
    /// Shows a folder browser dialog.
    /// </summary>
    string? ShowFolderBrowserDialog(string title, string? initialDirectory = null);
    
    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    bool ShowConfirmation(string title, string message);
    
    /// <summary>
    /// Shows an error message.
    /// </summary>
    void ShowError(string title, string message);
    
    /// <summary>
    /// Shows an information message.
    /// </summary>
    void ShowInfo(string title, string message);
    
    /// <summary>
    /// Shows a warning message.
    /// </summary>
    void ShowWarning(string title, string message);
    
    /// <summary>
    /// Shows a yes/no/cancel dialog.
    /// </summary>
    bool? ShowYesNoCancel(string title, string message);
    
    /// <summary>
    /// Shows a test case picker dialog for multi-selection.
    /// </summary>
    /// <param name="discovery">The discovery result containing available test cases.</param>
    /// <param name="excludeRefs">Optional list of test case refs to exclude (already in suite).</param>
    /// <returns>List of selected test case info (id, name, version, folderName, privilege) or empty if cancelled.</returns>
    IReadOnlyList<(string Id, string Name, string Version, string FolderName, PcTest.Contracts.Privilege Privilege)> ShowTestCasePicker(
        PcTest.Engine.Discovery.DiscoveryResult discovery,
        IEnumerable<string>? excludeRefs = null);
    
    /// <summary>
    /// Shows a test suite picker dialog for multi-selection.
    /// </summary>
    /// <param name="suites">List of available test suites.</param>
    /// <param name="excludeIdentities">Optional list of suite identities to exclude (already in plan).</param>
    /// <returns>List of selected suite identities (id@version) or empty if cancelled.</returns>
    IReadOnlyList<string> ShowSuitePicker(
        IEnumerable<PcTest.Ui.ViewModels.SuiteListItemViewModel> suites,
        IEnumerable<string>? excludeIdentities = null);

    /// <summary>
    /// Shows the history purge dialog.
    /// </summary>
    bool ShowPurgeHistoryDialog(PurgeHistoryViewModel viewModel);
}
