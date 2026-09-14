using FgScanner.App.Views;
using FgScanner.Core;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Shortcuts are bound on the main window, so they fire whichever section is showing. The router is
/// where a key is tied to the screen it belongs to (SPEC-2026-003 §08).
/// </summary>
public sealed class ShortcutRouterTests
{
    private static readonly string[] AllSections = ["Scan", "Groups", "Search", "Trash", "Settings"];

    private static readonly string[] PageKeys =
    [
        ShortcutMap.Actions.DeletePage,
        ShortcutMap.Actions.Undo,
        ShortcutMap.Actions.Redo,
        ShortcutMap.Actions.RotateLeft,
        ShortcutMap.Actions.RotateRight,
    ];

    public static TheoryData<string, string, ShortcutTarget> ScanKeysOnEverySection()
    {
        var data = new TheoryData<string, string, ShortcutTarget>();
        foreach (var section in AllSections)
        {
            data.Add(ShortcutMap.Actions.Scan, section, ShortcutTarget.Scan);
            data.Add(ShortcutMap.Actions.ScanAnnotated, section, ShortcutTarget.ScanAnnotated);
            data.Add(ShortcutMap.Actions.ScanNoteFace, section, ShortcutTarget.ScanNoteFace);
            data.Add(ShortcutMap.Actions.SaveToGroup, section, ShortcutTarget.SaveToGroup);
            data.Add(ShortcutMap.Actions.Commit, section, ShortcutTarget.GroupCommit);
            data.Add(ShortcutMap.Actions.Profile(3), section, ShortcutTarget.SelectProfile);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ScanKeysOnEverySection))]
    public void Scan_commit_and_profile_keys_work_from_every_section(string action, string section, ShortcutTarget expected) =>
        Assert.Equal(expected, ShortcutRouter.Route(action, section));

    [Theory]
    [InlineData(ShortcutMap.Actions.Undo, ShortcutTarget.GroupUndo)]
    [InlineData(ShortcutMap.Actions.Redo, ShortcutTarget.GroupRedo)]
    [InlineData(ShortcutMap.Actions.RotateLeft, ShortcutTarget.GroupRotateLeft)]
    [InlineData(ShortcutMap.Actions.RotateRight, ShortcutTarget.GroupRotateRight)]
    [InlineData(ShortcutMap.Actions.DeletePage, ShortcutTarget.GroupDeletePage)]
    public void Page_keys_on_Groups_act_on_the_open_group(string action, ShortcutTarget expected) =>
        Assert.Equal(expected, ShortcutRouter.Route(action, "Groups"));

    /// <summary>
    /// The bug this router exists for: with a group open in Groups and the Scan section showing,
    /// Delete trashed the group's selected page, out of sight.
    /// </summary>
    [Fact]
    public void Delete_on_Scan_never_touches_the_open_group() =>
        Assert.NotEqual(ShortcutTarget.GroupDeletePage, ShortcutRouter.Route(ShortcutMap.Actions.DeletePage, "Scan"));

    [Fact]
    public void Delete_on_Scan_is_for_the_scanned_pages_not_yet_saved() =>
        Assert.Equal(ShortcutTarget.ScanDeleteStagedPages, ShortcutRouter.Route(ShortcutMap.Actions.DeletePage, "Scan"));

    public static TheoryData<string, string> PageKeysOffGroups()
    {
        var data = new TheoryData<string, string>();
        foreach (var section in new[] { "Search", "Trash", "Settings" })
        {
            foreach (var action in PageKeys)
            {
                data.Add(action, section);
            }
        }

        foreach (var action in PageKeys.Where(a => a != ShortcutMap.Actions.DeletePage))
        {
            data.Add(action, "Scan");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PageKeysOffGroups))]
    public void Page_keys_do_nothing_where_there_is_no_page_to_act_on(string action, string section) =>
        Assert.Equal(ShortcutTarget.None, ShortcutRouter.Route(action, section));

    [Fact]
    public void Every_default_shortcut_is_bound()
    {
        foreach (var action in ShortcutMap.CreateDefault().Bindings.Keys)
        {
            Assert.True(ShortcutRouter.Handles(action), action);
        }
    }

    [Fact]
    public void An_unknown_action_is_not_bound() =>
        Assert.False(ShortcutRouter.Handles("NoSuchAction"));

    [Fact]
    public void Profile_keys_name_their_position_in_the_list()
    {
        Assert.True(ShortcutRouter.TryProfileIndex(ShortcutMap.Actions.Profile(11), out var index));
        Assert.Equal(11, index);
        Assert.False(ShortcutRouter.TryProfileIndex("ProfileX", out _));
    }
}
