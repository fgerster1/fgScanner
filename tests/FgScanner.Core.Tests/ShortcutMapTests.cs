using FgScanner.Core;
using Xunit;

namespace FgScanner.Core.Tests;

public class ShortcutMapTests
{
    [Fact]
    public void Defaults_match_naps2_conventions()
    {
        var map = ShortcutMap.CreateDefault();

        Assert.Equal("Ctrl+Enter", map.GestureFor(ShortcutMap.Actions.Scan));
        Assert.Equal("Ctrl+Z", map.GestureFor(ShortcutMap.Actions.Undo));
        Assert.Equal("Ctrl+Shift+Left", map.GestureFor(ShortcutMap.Actions.RotateLeft));
        Assert.Equal("F2", map.GestureFor(ShortcutMap.Actions.Profile(1)));
        Assert.Equal("F12", map.GestureFor(ShortcutMap.Actions.Profile(11)));
    }

    /// <summary>
    /// The annotated sheet needs one key of its own; the ordinary Scan key then takes the
    /// clean capture, because the sequence — not the operator — owns the NoteState.
    /// </summary>
    [Fact]
    public void The_annotated_sheet_has_its_own_default_gesture()
    {
        var map = ShortcutMap.CreateDefault();

        Assert.Equal("Ctrl+Shift+N", map.GestureFor(ShortcutMap.Actions.ScanAnnotated));
        Assert.Equal("Ctrl+Shift+F", map.GestureFor(ShortcutMap.Actions.ScanNoteFace));
    }

    [Fact]
    public void No_two_actions_share_a_default_gesture()
    {
        var map = ShortcutMap.CreateDefault();

        var gestures = map.Bindings.Values.Where(g => g.Length > 0).ToList();
        Assert.Equal(gestures.Count, gestures.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Overrides_round_trip_through_json()
    {
        var map = ShortcutMap.CreateDefault();
        map.Set(ShortcutMap.Actions.Scan, "F5");
        map.Set(ShortcutMap.Actions.Undo, ""); // unbind

        var restored = ShortcutMap.FromJson(map.ToJson());

        Assert.Equal("F5", restored.GestureFor(ShortcutMap.Actions.Scan));
        Assert.Null(restored.GestureFor(ShortcutMap.Actions.Undo));
        Assert.Equal("Ctrl+Y", restored.GestureFor(ShortcutMap.Actions.Redo)); // untouched default survives
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void Missing_or_corrupt_settings_fall_back_to_defaults(string? json)
    {
        var map = ShortcutMap.FromJson(json);

        Assert.Equal("Ctrl+Enter", map.GestureFor(ShortcutMap.Actions.Scan));
    }

    [Theory]
    [InlineData("Ctrl+Enter", new[] { "Ctrl" }, "Enter")]
    [InlineData("Ctrl+Shift+Left", new[] { "Ctrl", "Shift" }, "Left")]
    [InlineData("F5", new string[0], "F5")]
    [InlineData("ctrl+alt+D", new[] { "Ctrl", "Alt" }, "D")]
    public void Gestures_parse_into_modifiers_and_key(string gesture, string[] mods, string key)
    {
        Assert.True(ShortcutMap.TryParseGesture(gesture, out var parsedMods, out var parsedKey));
        Assert.Equal(mods, parsedMods);
        Assert.Equal(key, parsedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bogus+X")]
    public void Invalid_gestures_are_rejected(string gesture) =>
        Assert.False(ShortcutMap.TryParseGesture(gesture, out _, out _));
}
