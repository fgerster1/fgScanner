namespace FgScanner.App.Views;

/// <summary>
/// What a settings save moved, so a listener reloads only what it has to.
///
/// The alternative — a bare "something changed" signal — makes every save rebuild every
/// section's state, including the form fields an operator may be typing into. Naming the
/// parts keeps a profile rename from disturbing an open group's field editors.
/// </summary>
[Flags]
public enum SettingsChange
{
    None = 0,

    /// <summary>A profile was created, renamed, deleted, imported, or had its base folder set.</summary>
    Profiles = 1,

    /// <summary>A new index-schema version was minted by editing a profile's fields.</summary>
    Schema = 2,

    /// <summary>A feature flag changed — these decide whether whole sections and buttons exist.</summary>
    Flags = 4,

    /// <summary>The trash retention period changed.</summary>
    Retention = 8,
}
