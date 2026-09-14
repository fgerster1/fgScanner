using FgScanner.Core;

namespace FgScanner.App.Views;

/// <summary>What a shortcut acts on. The Group targets act on whichever group is open in Groups.</summary>
public enum ShortcutTarget
{
    None,
    Scan,
    ScanAnnotated,
    ScanNoteFace,
    SaveToGroup,
    GroupCommit,
    GroupUndo,
    GroupRedo,
    GroupRotateLeft,
    GroupRotateRight,
    GroupDeletePage,
    SelectProfile,
}

/// <summary>
/// Decides what a shortcut acts on. Free of WPF so that every key can be checked against every
/// section without a window.
/// </summary>
public static class ShortcutRouter
{
    private const string ProfilePrefix = "Profile";

    /// <summary>Whether the action is one this app binds at all, on any section.</summary>
    public static bool Handles(string action) => TargetOf(action) != ShortcutTarget.None;

    public static ShortcutTarget Route(string action, string section) => TargetOf(action);

    /// <summary>The N in "ProfileN", the Nth profile in the list (1-based).</summary>
    public static bool TryProfileIndex(string action, out int index)
    {
        index = 0;
        return action.StartsWith(ProfilePrefix, StringComparison.Ordinal)
            && int.TryParse(action[ProfilePrefix.Length..], out index);
    }

    private static ShortcutTarget TargetOf(string action) => action switch
    {
        ShortcutMap.Actions.Scan => ShortcutTarget.Scan,
        ShortcutMap.Actions.ScanAnnotated => ShortcutTarget.ScanAnnotated,
        ShortcutMap.Actions.ScanNoteFace => ShortcutTarget.ScanNoteFace,
        ShortcutMap.Actions.SaveToGroup => ShortcutTarget.SaveToGroup,
        ShortcutMap.Actions.Commit => ShortcutTarget.GroupCommit,
        ShortcutMap.Actions.Undo => ShortcutTarget.GroupUndo,
        ShortcutMap.Actions.Redo => ShortcutTarget.GroupRedo,
        ShortcutMap.Actions.RotateLeft => ShortcutTarget.GroupRotateLeft,
        ShortcutMap.Actions.RotateRight => ShortcutTarget.GroupRotateRight,
        ShortcutMap.Actions.DeletePage => ShortcutTarget.GroupDeletePage,
        _ when TryProfileIndex(action, out _) => ShortcutTarget.SelectProfile,
        _ => ShortcutTarget.None,
    };
}
