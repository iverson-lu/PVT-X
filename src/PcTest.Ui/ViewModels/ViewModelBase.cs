using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// Base class for all ViewModels using CommunityToolkit.Mvvm.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _busyMessage;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string? BusyMessage
    {
        get => _busyMessage;
        set => SetProperty(ref _busyMessage, value);
    }

    protected void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;
        BusyMessage = message;
    }
}

/// <summary>
/// Base class for ViewModels that support dirty state tracking.
/// </summary>
public abstract class EditableViewModelBase : ViewModelBase
{
    private bool _isDirty;
    private bool _isValid = true;
    private string? _validationMessage;

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnIsDirtyChanged();
            }
        }
    }

    protected virtual void OnIsDirtyChanged() { }

    public bool IsValid
    {
        get => _isValid;
        set => SetProperty(ref _isValid, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        set => SetProperty(ref _validationMessage, value);
    }

    protected void MarkDirty()
    {
        IsDirty = true;
    }

    protected void ClearDirty()
    {
        IsDirty = false;
    }

    public abstract void Validate();
    public abstract Task SaveAsync();
    public abstract void Discard();
}
