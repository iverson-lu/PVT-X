using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcTest.UI.ViewModels;

namespace PcTest.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnRunHistoryRowInvoked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: RunHistoryEntryViewModel entry })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.RunFolder))
        {
            return;
        }

        OpenRunFolder(entry.RunFolder);
    }

    private static void OpenRunFolder(string runFolder)
    {
        if (!Directory.Exists(runFolder))
        {
            MessageBox.Show($"Run folder not found: {runFolder}", "Run Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = runFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open run folder: {ex.Message}", "Run Folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
