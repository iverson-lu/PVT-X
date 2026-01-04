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
    private readonly IFileDialogService _fileDialogService;

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
        _fileDialogService = fileDialogService;

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

    /// <summary>
    /// Check if there are unsaved changes before switching tabs.
    /// Returns true if tab switch should proceed, false if cancelled.
    /// </summary>
    public bool CheckUnsavedChanges(int newTabIndex)
    {
        EditableViewModelBase? editor = null;
        string itemType = "";

        // Get the current editor based on current tab
        switch (SelectedTabIndex)
        {
            case 1: // Suites
                editor = SuitesTab.Editor;
                itemType = "suite";
                break;
            case 2: // Plans
                editor = PlansTab.Editor;
                itemType = "plan";
                break;
        }

        // If there's an editor with unsaved changes, prompt the user
        if (editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                $"You have unsaved changes to the current {itemType}. Do you want to save before switching?");

            if (result is null) // Cancel
            {
                return false;
            }
            else if (result == true) // Yes - save
            {
                _ = editor.SaveAsync();
            }
            else // No - discard
            {
                editor.Discard();
            }
        }

        return true;
    }

    /// <summary>
    /// Check if there are any unsaved changes in any tab.
    /// Returns true if it's safe to navigate away, false if cancelled.
    /// </summary>
    public bool CheckUnsavedChangesBeforeNavigate()
    {
        // Check each tab for unsaved changes
        if (SuitesTab.Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes to a suite. Do you want to save before leaving?");

            if (result is null) // Cancel
            {
                return false;
            }
            else if (result == true) // Yes - save
            {
                _ = SuitesTab.Editor.SaveAsync();
            }
            else // No - discard
            {
                SuitesTab.Editor.Discard();
            }
        }

        if (PlansTab.Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes to a plan. Do you want to save before leaving?");

            if (result is null) // Cancel
            {
                return false;
            }
            else if (result == true) // Yes - save
            {
                _ = PlansTab.Editor.SaveAsync();
            }
            else // No - discard
            {
                PlansTab.Editor.Discard();
            }
        }

        return true;
    }
}
