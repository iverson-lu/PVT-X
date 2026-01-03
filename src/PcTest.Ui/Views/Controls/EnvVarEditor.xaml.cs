using System.Windows.Controls;
using System.Windows.Input;

namespace PcTest.Ui.Views.Controls;

/// <summary>
/// Reusable control for editing environment variables (key-value pairs).
/// </summary>
public partial class EnvVarEditor : UserControl
{
    public EnvVarEditor()
    {
        InitializeComponent();
    }

    private void OnRowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item && !item.IsSelected)
        {
            item.IsSelected = true;
        }
    }
}
