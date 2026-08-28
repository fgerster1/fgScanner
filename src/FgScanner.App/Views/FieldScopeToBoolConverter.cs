using System.Globalization;
using System.Windows.Data;
using FgScanner.Core.Index;

namespace FgScanner.App.Views;

/// <summary>Binds the field editor's Batch checkbox to Scope; FieldScope.Batch is the only checked state.</summary>
public sealed class FieldScopeToBoolConverter : IValueConverter
{
    public static FieldScopeToBoolConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FieldScope.Batch;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FieldScope.Batch : FieldScope.Row;
}
