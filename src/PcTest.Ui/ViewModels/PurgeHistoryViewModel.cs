using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the history purge dialog.
/// </summary>
public partial class PurgeHistoryViewModel : ViewModelBase
{
    private readonly IRunRepository _runRepository;
    private readonly IFileDialogService _fileDialogService;

    [ObservableProperty]
    private int _days = 30;

    [ObservableProperty]
    private HistoryPurgePreview? _preview;

    [ObservableProperty]
    private bool _isPreviewing;

    [ObservableProperty]
    private bool _isPurging;

    public PurgeHistoryViewModel(IRunRepository runRepository, IFileDialogService fileDialogService)
    {
        _runRepository = runRepository;
        _fileDialogService = fileDialogService;
    }

    public bool HasPreview => Preview is not null;

    public bool ShowNoMatches => Preview is not null && Preview.RunCount == 0;

    public string PreviewRunCountDisplay => HasPreview ? Preview!.RunCount.ToString("N0") : "—";

    public string PreviewArtifactSizeDisplay => HasPreview ? FormatSize(Preview!.TotalArtifactSize) : "—";

    public string PreviewTimeRangeDisplay => HasPreview
        ? FormatTimeRange(Preview!.EarliestRunTime, Preview!.LatestRunTime)
        : "—";

    public bool CanPurge => HasPreview && Preview!.RunCount > 0 && !IsPurging && !IsPreviewing;

    public bool WasPurged { get; private set; }

    public HistoryPurgeResult? PurgeResult { get; private set; }

    partial void OnDaysChanged(int value)
    {
        if (value < 1)
        {
            Days = 1;
            return;
        }

        Preview = null;
    }

    partial void OnPreviewChanged(HistoryPurgePreview? value)
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(PreviewRunCountDisplay));
        OnPropertyChanged(nameof(PreviewArtifactSizeDisplay));
        OnPropertyChanged(nameof(PreviewTimeRangeDisplay));
        OnPropertyChanged(nameof(CanPurge));
    }

    partial void OnIsPurgingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPurge));
    }

    partial void OnIsPreviewingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPurge));
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (IsPreviewing)
        {
            return;
        }

        IsPreviewing = true;

        try
        {
            Preview = await _runRepository.PreviewHistoryPurgeAsync(Days);
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Preview Failed", ex.Message);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    public async Task<bool> PurgeAsync()
    {
        if (!CanPurge)
        {
            return false;
        }

        IsPurging = true;

        try
        {
            PurgeResult = await _runRepository.PurgeHistoryAsync(Days);

            if (PurgeResult.RunCount == 0)
            {
                _fileDialogService.ShowInfo("No Runs Purged", "No runs match the purge criteria.");
                Preview = PurgeResult;
                return false;
            }

            WasPurged = true;
            return true;
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Purge Failed", ex.Message);
            return false;
        }
        finally
        {
            IsPurging = false;
        }
    }

    private static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "N/A";
        }

        if (bytes.Value < 1024)
        {
            return $"{bytes.Value:N0} B";
        }

        if (bytes.Value < 1024 * 1024)
        {
            return $"{bytes.Value / 1024.0:N1} KB";
        }

        if (bytes.Value < 1024L * 1024L * 1024L)
        {
            return $"{bytes.Value / (1024.0 * 1024.0):N1} MB";
        }

        return $"{bytes.Value / (1024.0 * 1024.0 * 1024.0):N1} GB";
    }

    private static string FormatTimeRange(DateTime? earliest, DateTime? latest)
    {
        if (!earliest.HasValue || !latest.HasValue)
        {
            return "—";
        }

        return $"{earliest.Value:yyyy-MM-dd HH:mm} – {latest.Value:yyyy-MM-dd HH:mm}";
    }
}
