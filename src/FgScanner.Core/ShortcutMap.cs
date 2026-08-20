using System.Text.Json;

namespace FgScanner.Core;

/// <summary>
/// Rebindable keyboard shortcuts (PLAN §5.8), NAPS2 defaults. Gestures are plain strings
/// ("Ctrl+Enter", "F2", "Ctrl+Shift+Left") so the map serializes to settings and stays
/// UI-framework-free; the WPF layer converts them to KeyGestures.
/// </summary>
public sealed class ShortcutMap
{
    /// <summary>Action ids, stable across releases (they key the stored overrides).</summary>
    public static class Actions
    {
        public const string Scan = "Scan";
        public const string SaveToGroup = "SaveToGroup";
        public const string Commit = "Commit";
        public const string Undo = "Undo";
        public const string Redo = "Redo";
        public const string RotateLeft = "RotateLeft";
        public const string RotateRight = "RotateRight";
        public const string DeletePage = "DeletePage";

        /// <summary>Profile1..Profile11 select the Nth profile (NAPS2's F2–F12 convention).</summary>
        public static string Profile(int index) => $"Profile{index}";
    }

    private readonly Dictionary<string, string> _map;

    private ShortcutMap(Dictionary<string, string> map) => _map = map;

    public static ShortcutMap CreateDefault()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Actions.Scan] = "Ctrl+Enter",
            [Actions.SaveToGroup] = "Ctrl+S",
            [Actions.Commit] = "Ctrl+Shift+Enter",
            [Actions.Undo] = "Ctrl+Z",
            [Actions.Redo] = "Ctrl+Y",
            [Actions.RotateLeft] = "Ctrl+Shift+Left",
            [Actions.RotateRight] = "Ctrl+Shift+Right",
            [Actions.DeletePage] = "Delete",
        };
        for (var i = 1; i <= 11; i++)
        {
            map[Actions.Profile(i)] = $"F{i + 1}"; // F2..F12
        }

        return new ShortcutMap(map);
    }

    public IReadOnlyDictionary<string, string> Bindings => _map;

    public string? GestureFor(string action) =>
        _map.GetValueOrDefault(action) is { Length: > 0 } gesture ? gesture : null;

    /// <summary>Rebinds one action; an empty gesture unbinds it (and the unbind persists).</summary>
    public void Set(string action, string gesture) =>
        _map[action] = string.IsNullOrWhiteSpace(gesture) ? "" : gesture.Trim();

    public string ToJson() => JsonSerializer.Serialize(_map);

    /// <summary>Defaults overlaid with stored user overrides; unknown actions are kept (forward compat).</summary>
    public static ShortcutMap FromJson(string? json)
    {
        var map = CreateDefault();
        if (string.IsNullOrWhiteSpace(json))
        {
            return map;
        }

        try
        {
            foreach (var (action, gesture) in
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
            {
                map.Set(action, gesture);
            }
        }
        catch (JsonException)
        {
            // A corrupt setting falls back to defaults rather than breaking startup.
        }

        return map;
    }

    /// <summary>Parses "Ctrl+Shift+X" into modifier names + key name; false for empty/garbage.</summary>
    public static bool TryParseGesture(
        string gesture, out IReadOnlyList<string> modifiers, out string key)
    {
        modifiers = [];
        key = "";
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var knownModifiers = new[] { "Ctrl", "Shift", "Alt", "Win" };
        var mods = parts[..^1];
        if (mods.Any(m => !knownModifiers.Contains(m, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        modifiers = [.. mods.Select(m =>
            knownModifiers.First(k => k.Equals(m, StringComparison.OrdinalIgnoreCase)))];
        key = parts[^1];
        return key.Length > 0;
    }
}
