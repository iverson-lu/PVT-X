using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Behaviors;

/// <summary>
/// Attached behavior for managing multi-select CheckBox synchronization with JSON array CurrentValue.
/// </summary>
public static class MultiSelectJsonBehavior
{
    public static readonly DependencyProperty SyncWithJsonProperty =
        DependencyProperty.RegisterAttached(
            "SyncWithJson",
            typeof(bool),
            typeof(MultiSelectJsonBehavior),
            new PropertyMetadata(false, OnSyncWithJsonChanged));

    public static bool GetSyncWithJson(DependencyObject obj)
        => (bool)obj.GetValue(SyncWithJsonProperty);

    public static void SetSyncWithJson(DependencyObject obj, bool value)
        => obj.SetValue(SyncWithJsonProperty, value);

    private static void OnSyncWithJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CheckBox checkBox)
            return;

        if ((bool)e.NewValue)
        {
            checkBox.Checked += OnCheckBoxChanged;
            checkBox.Unchecked += OnCheckBoxChanged;
        }
        else
        {
            checkBox.Checked -= OnCheckBoxChanged;
            checkBox.Unchecked -= OnCheckBoxChanged;
        }
    }

    private static void OnCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        // Get the option value from CheckBox.Content
        if (checkBox.Content is not string optionValue)
            return;

        // Get the ParameterViewModel from ItemsControl's DataContext
        var itemsControl = FindParent<ItemsControl>(checkBox);
        if (itemsControl?.DataContext is not ParameterViewModel paramVm)
            return;

        try
        {
            // Parse current JSON array
            var currentArray = string.IsNullOrWhiteSpace(paramVm.CurrentValue)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(paramVm.CurrentValue) ?? new List<string>();

            // Update array based on checkbox state
            if (checkBox.IsChecked == true)
            {
                if (!currentArray.Contains(optionValue))
                {
                    currentArray.Add(optionValue);
                }
            }
            else
            {
                currentArray.Remove(optionValue);
            }

            // Serialize back to JSON and update CurrentValue
            paramVm.CurrentValue = JsonSerializer.Serialize(currentArray);
        }
        catch
        {
            // If parsing fails, reset to single-item array or empty
            if (checkBox.IsChecked == true)
            {
                paramVm.CurrentValue = JsonSerializer.Serialize(new List<string> { optionValue });
            }
            else
            {
                paramVm.CurrentValue = "[]";
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        
        return null;
    }
}
