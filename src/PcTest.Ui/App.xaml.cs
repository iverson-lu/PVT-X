using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcTest.Engine;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using PcTest.Ui.Views;
using PcTest.Ui.Views.Pages;

namespace PcTest.Ui;

/// <summary>
/// PC Test System UI Application.
/// </summary>
public partial class App : Application
{
    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureServices((context, services) =>
        {
            // Core services
            services.AddSingleton<TestEngine>();
            
            // UI services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IDiscoveryService, DiscoveryServiceAdapter>();
            services.AddSingleton<ISuiteRepository, SuiteRepository>();
            services.AddSingleton<IPlanRepository, PlanRepository>();
            services.AddSingleton<IRunService, RunService>();
            services.AddSingleton<IRunRepository, RunRepository>();
            
            // ViewModels
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<PlanViewModel>();
            services.AddTransient<CasesTabViewModel>();
            services.AddTransient<SuitesTabViewModel>();
            services.AddTransient<SuiteEditorViewModel>();
            services.AddTransient<PlansTabViewModel>();
            services.AddTransient<PlanEditorViewModel>();
            services.AddTransient<RunViewModel>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<LogsResultsViewModel>();
            services.AddTransient<RunPickerViewModel>();
            services.AddTransient<SettingsViewModel>();
            
            // Views
            services.AddSingleton<MainWindow>();
            services.AddTransient<PlanPage>();
            services.AddTransient<RunPage>();
            services.AddTransient<HistoryPage>();
            services.AddTransient<LogsResultsPage>();
            services.AddTransient<SettingsPage>();
        })
        .Build();

    public static IServiceProvider Services => _host.Services;

    public static T GetService<T>() where T : class
        => _host.Services.GetRequiredService<T>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        
        // Initialize settings
        var settingsService = GetService<ISettingsService>();
        await settingsService.LoadAsync();
        
        // Apply theme
        ApplyTheme(settingsService.CurrentSettings.Theme);
        
        var mainWindow = GetService<MainWindow>();
        mainWindow.Show();
        
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "PC Test System Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void ApplyTheme(string theme)
    {
        var appTheme = theme?.ToLowerInvariant() switch
        {
            "light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            "dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Dark
        };
        
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(appTheme);
    }
}
