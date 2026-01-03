using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Views.Controls;

/// <summary>
/// Reusable control for editing environment variables (key-value pairs).
/// </summary>
public partial class EnvVarEditor : UserControl
{
    public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
        nameof(HintText),
        typeof(string),
        typeof(EnvVarEditor),
        new PropertyMetadata(string.Empty));
    
    public string HintText
    {
        get => (string)GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }
    
    public EnvVarEditor()
    {
        InitializeComponent();
    }
    
    private void OnDeleteButtonPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not EnvVarEditorViewModel viewModel)
            return;
        
        if (sender is Button button && button.DataContext is EnvVarRowViewModel row)
        {
            viewModel.SelectedItem = row;
        }
    }
}
