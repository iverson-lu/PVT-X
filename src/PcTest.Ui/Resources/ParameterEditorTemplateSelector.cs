using System.Windows;
using System.Windows.Controls;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Resources;

/// <summary>
/// Selects appropriate DataTemplate for parameter editors based on parameter type.
/// </summary>
public class ParameterEditorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? EnumEditorTemplate { get; set; }
    public DataTemplate? MultiSelectEditorTemplate { get; set; }
    public DataTemplate? BooleanToggleTemplate { get; set; }
    public DataTemplate? BooleanCheckBoxTemplate { get; set; }
    public DataTemplate? DefaultEditorTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ParameterViewModel param)
            return DefaultEditorTemplate ?? base.SelectTemplate(item, container);

        // MultiSelect (json + enumValues) → CheckBox list
        if (param.IsMultiSelect)
        {
            return MultiSelectEditorTemplate ?? DefaultEditorTemplate ?? base.SelectTemplate(item, container);
        }

        // Enum type → ComboBox
        if (param.IsEnum && param.EnumValues != null && param.EnumValues.Count > 0)
        {
            return EnumEditorTemplate ?? DefaultEditorTemplate ?? base.SelectTemplate(item, container);
        }

        // Boolean type → Toggle or plain CheckBox
        if (param.IsBoolean)
        {
            if (param.UsePlainCheckBox)
            {
                return BooleanCheckBoxTemplate ?? DefaultEditorTemplate ?? base.SelectTemplate(item, container);
            }
            return BooleanToggleTemplate ?? DefaultEditorTemplate ?? base.SelectTemplate(item, container);
        }

        // Default → TextBox (for string, int, double, path, json, etc.)
        return DefaultEditorTemplate ?? base.SelectTemplate(item, container);
    }
}
