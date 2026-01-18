using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PcTest.Ui.Resources;

/// <summary>
/// Converts boolean to Visibility.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Converts boolean to Visibility (inverted).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return true;
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts null to Visibility (null = Collapsed, non-null = Visible).
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null to inverse Visibility (null = Visible, non-null = Collapsed).
/// </summary>
public class NullToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts string to bool (empty/null = false, non-empty = true).
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts string to Visibility (empty/null = Collapsed, non-empty = Visible).
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to Opacity (true = 1.0, false = 0.0).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? 1.0 : 0.0;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double opacity)
        {
            return opacity > 0.5;
        }
        return false;
    }
}

/// <summary>
/// Converts boolean to Opacity (inverted: true = 0.0, false = 1.0).
/// </summary>
public class InverseBoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? 0.0 : 1.0;
        }
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double opacity)
        {
            return opacity <= 0.5;
        }
        return true;
    }
}

/// <summary>
/// Converts enum to int for ComboBox SelectedIndex binding.
/// </summary>
public class EnumToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return (int)(object)enumValue;
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && targetType.IsEnum)
        {
            return Enum.ToObject(targetType, intValue);
        }
        return Enum.ToObject(targetType, 0);
    }
}

/// <summary>
/// Converts RunType enum to icon symbol name for Fluent UI.
/// </summary>
public class RunTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PcTest.Contracts.RunType runType)
        {
            return runType switch
            {
                PcTest.Contracts.RunType.TestCase => "Document24",
                PcTest.Contracts.RunType.TestSuite => "Folder24",
                PcTest.Contracts.RunType.TestPlan => "Board24",
                _ => "Question24"
            };
        }
        return "Question24";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunStatus enum to status badge color/brush.
/// </summary>
public class RunStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PcTest.Contracts.RunStatus status)
        {
            return status switch
            {
                PcTest.Contracts.RunStatus.Passed => "#10B981",  // Green
                PcTest.Contracts.RunStatus.Failed => "#EF4444",  // Red
                PcTest.Contracts.RunStatus.Error => "#F59E0B",   // Orange
                PcTest.Contracts.RunStatus.Timeout => "#F97316", // Orange-red
                PcTest.Contracts.RunStatus.Aborted => "#6B7280", // Gray
                PcTest.Contracts.RunStatus.RebootRequired => "#8B5CF6", // Purple
                _ => "#9CA3AF"  // Light gray
            };
        }
        return "#9CA3AF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunStatus enum to short display text.
/// </summary>
public class RunStatusToShortTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PcTest.Contracts.RunStatus status)
        {
            return status switch
            {
                PcTest.Contracts.RunStatus.Passed => "Pass",
                PcTest.Contracts.RunStatus.Failed => "Fail",
                PcTest.Contracts.RunStatus.Error => "Error",
                PcTest.Contracts.RunStatus.Timeout => "Time",
                PcTest.Contracts.RunStatus.Aborted => "Stop",
                PcTest.Contracts.RunStatus.RebootRequired => "Reboot",
                _ => "?"
            };
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunStatus enum to icon symbol name.
/// </summary>
public class RunStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PcTest.Contracts.RunStatus status)
        {
            return status switch
            {
                PcTest.Contracts.RunStatus.Passed => "CheckmarkCircle24",
                PcTest.Contracts.RunStatus.Failed => "DismissCircle24",
                PcTest.Contracts.RunStatus.Error => "ErrorCircle24",
                PcTest.Contracts.RunStatus.Timeout => "Clock24",
                PcTest.Contracts.RunStatus.Aborted => "RecordStop24",
                PcTest.Contracts.RunStatus.RebootRequired => "ArrowClockwise24",
                _ => "Hourglass24"
            };
        }
        return "Hourglass24";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts tree depth to left margin for indentation.
/// Default multiplier is 16px per level.
/// </summary>
public class DepthToIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int depth)
        {
            var multiplier = 16.0;
            if (parameter is string paramStr && double.TryParse(paramStr, out var customMultiplier))
            {
                multiplier = customMultiplier;
            }
            return new Thickness(depth * multiplier, 0, 0, 0);
        }
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean HasChildren to Visibility for expand/collapse arrow.
/// </summary>
public class HasChildrenToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool hasChildren)
        {
            return hasChildren ? Visibility.Visible : Visibility.Hidden;
        }
        return Visibility.Hidden;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IsExpanded to expand/collapse icon symbol.
/// </summary>
public class IsExpandedToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "ChevronDown20" : "ChevronRight20";
        }
        return "ChevronRight20";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts an enum value to a boolean for RadioButton binding.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        
        var parameterString = parameter.ToString();
        if (string.IsNullOrEmpty(parameterString))
            return false;
        
        return value.ToString() == parameterString;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter != null)
        {
            var parameterString = parameter.ToString();
            if (!string.IsNullOrEmpty(parameterString) && Enum.TryParse(targetType, parameterString, out var result))
            {
                return result;
            }
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts an enum value to Visibility based on matching a parameter.
/// </summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Visibility.Collapsed;
        
        var parameterString = parameter.ToString();
        if (string.IsNullOrEmpty(parameterString))
            return Visibility.Collapsed;
        
        return value.ToString() == parameterString ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts between boolean string values ("true"/"false") and bool for CheckBox binding.
/// Supports "true"/"false", "1"/"0" (case-insensitive).
/// </summary>
public class BooleanStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string strValue)
        {
            var lower = strValue.ToLowerInvariant();
            return lower == "true" || lower == "1";
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "true" : "false";
        }
        return "false";
    }
}

/// <summary>
/// Converts event level string to appropriate color brush.
/// </summary>
public class EventLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string level)
        {
            return level.ToLowerInvariant() switch
            {
                "error" => "#EF4444",   // Red
                "warning" => "#F59E0B", // Orange
                "info" => "#3B82F6",    // Blue
                "debug" => "#9CA3AF",   // Gray
                "trace" => "#6B7280",   // Dark gray
                _ => "#9CA3AF"          // Default gray
            };
        }
        return "#9CA3AF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts event Code to a friendly display title.
/// Examples: "TestCase.Started" -> "Test Case Started"
/// </summary>
public class EventCodeToTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string code || string.IsNullOrEmpty(code))
            return string.Empty;

        // Replace dots with spaces and split on capital letters
        var parts = code.Split('.');
        var result = new System.Text.StringBuilder();
        
        foreach (var part in parts)
        {
            if (result.Length > 0)
                result.Append(' ');
            
            // Insert spaces before capital letters
            for (int i = 0; i < part.Length; i++)
            {
                if (i > 0 && char.IsUpper(part[i]))
                    result.Append(' ');
                result.Append(part[i]);
            }
        }
        
        return result.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Determines if an event code represents a "Started" event.
/// </summary>
public class EventCodeIsStartedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string code)
            return false;
        
        return code.EndsWith(".Started", StringComparison.OrdinalIgnoreCase) ||
               code.EndsWith(".Begin", StringComparison.OrdinalIgnoreCase) ||
               code.Contains("Starting", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Determines if an event code represents a "Completed" event.
/// </summary>
public class EventCodeIsCompletedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string code)
            return false;
        
        return code.EndsWith(".Completed", StringComparison.OrdinalIgnoreCase) ||
               code.EndsWith(".Finished", StringComparison.OrdinalIgnoreCase) ||
               code.EndsWith(".End", StringComparison.OrdinalIgnoreCase) ||
               code.Contains("Complete", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts event Level to a corresponding icon glyph (Segoe MDL2 Assets).
/// </summary>
public class EventLevelToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string level)
            return "\uE946"; // Default: StatusCircleBlock2
        
        return level.ToLowerInvariant() switch
        {
            "error" => "\uE711", // ErrorBadge
            "warning" => "\uE7BA", // Warning
            "info" => "\uE946", // StatusCircleBlock2
            "debug" => "\uE8EC", // DeveloperTools
            _ => "\uE946"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MultiBinding converter that checks if a JSON array string contains a specific value.
/// values[0] = JSON array string (e.g., "[\"OptionA\", \"OptionC\"]")
/// values[1] = value to check (e.g., "OptionA")
/// Returns true if the value is in the array.
/// </summary>
public class JsonArrayContainsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2)
            return false;

        if (values[0] is not string jsonString || string.IsNullOrWhiteSpace(jsonString))
            return false;

        if (values[1] is not string targetValue)
            return false;

        try
        {
            var array = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonString);
            return array?.Contains(targetValue) ?? false;
        }
        catch
        {
            return false;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a JSON array string to the count of selected items.
/// Used to display "X selected" in multi-select expander header.
/// </summary>
public class JsonArrayCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string jsonString || string.IsNullOrWhiteSpace(jsonString))
            return "0 selected";

        try
        {
            var array = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonString);
            var count = array?.Count ?? 0;
            return count == 1 ? "1 selected" : $"{count} selected";
        }
        catch
        {
            return "0 selected";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a JSON array string to a comma-separated display string.
/// Used to display selected values in multi-select expander header.
/// Example: ["OptionA", "OptionC"] -> "OptionA, OptionC"
/// </summary>
public class JsonArrayToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string jsonString || string.IsNullOrWhiteSpace(jsonString))
            return "(none)";

        try
        {
            var array = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonString);
            if (array == null || array.Count == 0)
                return "(none)";
            
            return string.Join(", ", array);
        }
        catch
        {
            return "(none)";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
