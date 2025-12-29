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
}
