using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Plan page (main workspace).
/// </summary>
public partial class PlanViewModel : ViewModelBase
{
    private readonly IDiscoveryService _discoveryService;
    private readonly ISuiteRepository _suiteRepository;
    private readonly IPlanRepository _planRepository;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private CasesTabViewModel _casesTab;

    [ObservableProperty]
    private SuitesTabViewModel _suitesTab;

    [ObservableProperty]
    private PlansTabViewModel _plansTab;

    public PlanViewModel(
        IDiscoveryService discoveryService,
        ISuiteRepository suiteRepository,
        IPlanRepository planRepository,
        INavigationService navigationService,
        IFileDialogService fileDialogService,
        IFileSystemService fileSystemService)
    {
        _discoveryService = discoveryService;
        _suiteRepository = suiteRepository;
        _planRepository = planRepository;
        _navigationService = navigationService;

        CasesTab = new CasesTabViewModel(discoveryService, fileSystemService, navigationService);
        SuitesTab = new SuitesTabViewModel(suiteRepository, discoveryService, fileDialogService, navigationService);
        PlansTab = new PlansTabViewModel(planRepository, suiteRepository, discoveryService, fileDialogService, navigationService);
    }

    public async Task InitializeAsync()
    {
        SetBusy(true, "Loading assets...");
        
        try
        {
            await CasesTab.LoadAsync();
            await SuitesTab.LoadAsync();
            await PlansTab.LoadAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }
}
