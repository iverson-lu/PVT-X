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
                PcTest.Contracts.RunStatus.Passed => "CheckmarkCircle20",
                PcTest.Contracts.RunStatus.Failed => "DismissCircle20",
                PcTest.Contracts.RunStatus.Error => "ErrorCircle20",
                PcTest.Contracts.RunStatus.Timeout => "Clock20",
                PcTest.Contracts.RunStatus.Aborted => "RecordStop20",
                _ => "Hourglass20"
            };
        }
        return "Hourglass20";
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
