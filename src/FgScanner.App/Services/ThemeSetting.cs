using System.Windows;

namespace FgScanner.App.Services;

/// <summary>
/// The light/dark setting, in one place. It used to be a magic string written by the first-run
/// wizard and read once at startup, with no control anywhere in Settings — so whatever the
/// operator picked on first launch was what they kept (SPEC-2026-004 §04 row 11).
/// </summary>
public static class ThemeSetting
{
    public const string Key = "Ui.Theme";

    /// <summary>"system" | "light" | "dark" — the values the first-run dialog also writes.</summary>
    public static IReadOnlyList<string> Choices { get; } = ["system", "light", "dark"];

    /// <summary>
    /// Applies the theme to the running application. Does nothing when there is no application —
    /// unit tests construct view models without a WPF app, and a theme is not what they assert.
    /// </summary>
    public static void Apply(string theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
