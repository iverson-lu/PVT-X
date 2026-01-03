using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for editing environment variables (key-value pairs).
/// Provides add/remove functionality with validation for unique, non-empty keys.
/// 
/// Manual Test Notes:
/// 1. Add new keys: Click Add button, enter key/value, verify it appears in list
/// 2. Edit existing keys: Click on row, modify key or value, verify changes persist
/// 3. Remove keys: Select row, click Remove button, verify row is deleted
/// 4. Duplicate key validation: Try to add or edit a key to match an existing key (case-insensitive), 
///    verify error message appears and hasValidationError is true
/// 5. Empty key validation: Try to create a row with empty key, verify error message appears
/// 6. Persistence: Add/edit/remove keys, save manifest, close and reopen, verify changes persisted
/// 7. Value can be empty: Create a row with empty value, verify it's treated as valid
/// </summary>
public partial class EnvVarEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<EnvVarRowViewModel> _items = new();

    [ObservableProperty]
    private EnvVarRowViewModel? _selectedItem;

    [ObservableProperty]
    private bool _hasValidationError;

    /// <summary>
    /// Callback invoked when any row changes (for dirty tracking in parent).
    /// </summary>
    public Action? OnRowChanged { get; set; }

    public EnvVarEditorViewModel()
    {
    }

    /// <summary>
    /// Loads environment variables from a dictionary.
    /// </summary>
    public void LoadFromDictionary(Dictionary<string, string>? env)
    {
        Items.Clear();

        if (env is not null)
        {
            foreach (var (key, value) in env.OrderBy(kvp => kvp.Key))
            {
                var row = new EnvVarRowViewModel
                {
                    Key = key,
                    Value = value
                };
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }
        }

        ValidateAll();
    }

    /// <summary>
    /// Exports environment variables to a dictionary.
    /// Returns null if no environment variables are defined.
    /// </summary>
    public Dictionary<string, string>? ToDictionary()
    {
        if (Items.Count == 0)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var item in Items)
        {
            var key = item.Key?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = item.Value ?? string.Empty;
            }
        }

        return result.Count > 0 ? result : null;
    }

    [RelayCommand]
    private void AddRow()
    {
        var newRow = new EnvVarRowViewModel
        {
            Key = string.Empty,
            Value = string.Empty
        };
        newRow.PropertyChanged += OnRowPropertyChanged;
        Items.Add(newRow);
        SelectedItem = newRow;
        ValidateAll();
        OnRowChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveRow(EnvVarRowViewModel? row)
    {
        var item = row ?? SelectedItem;
        if (item is null) return;

        item.PropertyChanged -= OnRowPropertyChanged;
        Items.Remove(item);
        SelectedItem = Items.FirstOrDefault();
        ValidateAll();
        OnRowChanged?.Invoke();
    }

    /// <summary>
    /// Validates all rows for duplicate/empty keys.
    /// </summary>
    public void ValidateAll()
    {
        // Clear all errors first
        foreach (var item in Items)
        {
            item.HasError = false;
            item.Error = null;
        }

        var keyGroups = Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Key))
            .GroupBy(i => i.Key!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Check for duplicates
        foreach (var group in keyGroups.Where(g => g.Count() > 1))
        {
            foreach (var item in group)
            {
                item.HasError = true;
                item.Error = "Duplicate key";
            }
        }

        // Check for empty keys
        foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Key)))
        {
            item.HasError = true;
            item.Error = "Key is required";
        }

        HasValidationError = Items.Any(i => i.HasError);
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EnvVarRowViewModel.Key) || e.PropertyName == nameof(EnvVarRowViewModel.Value))
        {
            ValidateAll();
            OnRowChanged?.Invoke();
        }
    }
}

/// <summary>
/// ViewModel for a single environment variable row (key-value pair).
/// </summary>
public partial class EnvVarRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _key;

    [ObservableProperty]
    private string? _value;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _error;
}
