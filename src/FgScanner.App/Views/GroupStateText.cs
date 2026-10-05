using System.Globalization;
using System.Windows.Data;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// A group's state as the operator reads it. The stored name "Scanning" read as "something is
/// running" (Franz, 2026-10-05), when it only means the group is still open; the stored value keeps
/// its name, because renaming an enum member renames what the database and exports hold.
/// </summary>
public static class GroupStateText
{
    public static string Of(GroupState state) => state == GroupState.Committed ? "Committed" : "Open";
}

/// <summary>The group list's state line, via <see cref="GroupStateText"/>.</summary>
public sealed class GroupStateConverter : IValueConverter
{
    public static GroupStateConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GroupState state ? GroupStateText.Of(state) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
