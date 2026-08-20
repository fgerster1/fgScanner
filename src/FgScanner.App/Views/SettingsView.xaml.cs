using System.Windows.Controls;
using FgScanner.Data;

namespace FgScanner.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        TypeColumn.ItemsSource = Enum.GetValues<FieldType>();
    }
}
