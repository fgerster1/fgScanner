using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace FgScanner.App.Views;

/// <summary>
/// "Page N" for a thumbnail's place in the list, so the labels close up after pages are deleted.
/// The scan's sequence number keeps its gaps: it names the file, not the page's position.
///
/// Values: the page, the list, then the list's Count. The Count is bound only so WPF re-evaluates
/// the label whenever the list changes.
/// </summary>
public sealed class PagePositionConverter : IMultiValueConverter
{
    public static PagePositionConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [var item, IList list, ..] && list.IndexOf(item) is var index and >= 0
            ? "Page " + (index + 1).ToString(CultureInfo.InvariantCulture)
            : "";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
