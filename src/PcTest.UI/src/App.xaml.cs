using System.Windows;

namespace PcTest.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// Hooks unhandled exception logging to surface errors that would otherwise close the app silently.
    /// </summary>
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
