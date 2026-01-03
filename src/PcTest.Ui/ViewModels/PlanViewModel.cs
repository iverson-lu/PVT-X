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

    public void SelectItemByIdentity(int tabIndex, string? targetIdentity)
    {
        if (string.IsNullOrEmpty(targetIdentity)) return;

        switch (tabIndex)
        {
            case 0: // Cases
                var caseItem = CasesTab.Cases.FirstOrDefault(c => c.Identity == targetIdentity);
                if (caseItem != null)
                {
                    CasesTab.SelectedCase = caseItem;
                }
                break;
            case 1: // Suites
                var suiteItem = SuitesTab.Suites.FirstOrDefault(s => s.Identity == targetIdentity);
                if (suiteItem != null)
                {
                    SuitesTab.SelectedSuite = suiteItem;
                }
                break;
            case 2: // Plans
                var planItem = PlansTab.Plans.FirstOrDefault(p => p.Identity == targetIdentity);
                if (planItem != null)
                {
                    PlansTab.SelectedPlan = planItem;
                }
                break;
        }
    }
}
