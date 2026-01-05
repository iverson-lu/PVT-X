using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for purging history runs.
/// </summary>
public sealed partial class PurgeHistoryViewModel : ObservableObject
{
    private readonly IRunRepository _runRepository;

    public PurgeHistoryViewModel(IRunRepository runRepository)
    {
        _runRepository = runRepository;
    }

    public event Action<bool?>? CloseRequested;

    [ObservableProperty]
    private int _days = 30;

    [ObservableProperty]
    private bool _isPreviewInProgress;

    [ObservableProperty]
    private bool _isPurgeInProgress;

    [ObservableProperty]
    private bool _isPreviewReady;

    [ObservableProperty]
    private int _previewRunCount;

    [ObservableProperty]
    private string _previewArtifactSize = "—";

    [ObservableProperty]
    private string _previewTimeRange = "—";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool CanPurge => IsPreviewReady && PreviewRunCount > 0 && !IsPurgeInProgress;

    public bool HasNoMatches => IsPreviewReady && PreviewRunCount == 0;

    partial void OnDaysChanged(int value)
    {
        if (value < 1)
        {
            Days = 1;
            return;
        }

        InvalidatePreview();
    }

    partial void OnIsPreviewReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPurge));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    partial void OnPreviewRunCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanPurge));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    partial void OnIsPurgeInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPurge));
    }

    private void InvalidatePreview()
    {
        IsPreviewReady = false;
        PreviewRunCount = 0;
        PreviewArtifactSize = "—";
        PreviewTimeRange = "—";
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        ErrorMessage = string.Empty;
        IsPreviewInProgress = true;

        try
        {
            var preview = await _runRepository.PreviewPurgeAsync(Days);
            PreviewRunCount = preview.RunCount;
            PreviewArtifactSize = preview.TotalArtifactSize.HasValue
                ? FormatBytes(preview.TotalArtifactSize.Value)
                : "Unavailable";
            PreviewTimeRange = FormatTimeRange(preview.EarliestRunTime, preview.LatestRunTime);
            IsPreviewReady = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
            IsPreviewReady = false;
        }
        finally
        {
            IsPreviewInProgress = false;
        }
    }

    [RelayCommand]
    private async Task PurgeAsync()
    {
        if (!CanPurge)
        {
            return;
        }

        ErrorMessage = string.Empty;
        IsPurgeInProgress = true;

        try
        {
            await _runRepository.PurgeHistoryAsync(Days);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Purge failed: {ex.Message}";
        }
        finally
        {
            IsPurgeInProgress = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    private static string FormatTimeRange(DateTime? earliest, DateTime? latest)
    {
        if (!earliest.HasValue || !latest.HasValue)
        {
            return "—";
        }

        return $"{earliest:yyyy-MM-dd HH:mm} → {latest:yyyy-MM-dd HH:mm}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
