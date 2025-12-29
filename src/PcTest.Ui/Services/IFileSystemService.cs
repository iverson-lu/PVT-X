using System.IO;

namespace PcTest.Ui.Services;

/// <summary>
/// Abstraction over file system operations.
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Reads all text from a file.
    /// </summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reads all lines from a file.
    /// </summary>
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Writes all text to a file.
    /// </summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a file exists.
    /// </summary>
    bool FileExists(string path);
    
    /// <summary>
    /// Checks if a directory exists.
    /// </summary>
    bool DirectoryExists(string path);
    
    /// <summary>
    /// Creates a directory.
    /// </summary>
    void CreateDirectory(string path);
    
    /// <summary>
    /// Deletes a file.
    /// </summary>
    void DeleteFile(string path);
    
    /// <summary>
    /// Deletes a directory.
    /// </summary>
    void DeleteDirectory(string path, bool recursive = false);
    
    /// <summary>
    /// Gets files in a directory.
    /// </summary>
    IEnumerable<string> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
    
    /// <summary>
    /// Gets directories in a directory.
    /// </summary>
    IEnumerable<string> GetDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
    
    /// <summary>
    /// Gets file info.
    /// </summary>
    FileInfo GetFileInfo(string path);
    
    /// <summary>
    /// Opens a folder in Explorer.
    /// </summary>
    void OpenInExplorer(string path);
    
    /// <summary>
    /// Opens a file with the default application.
    /// </summary>
    void OpenWithDefaultApp(string path);
}
