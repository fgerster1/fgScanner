using System.Text.Json;
using System.Text.Json.Serialization;
using FgScanner.Data;

namespace FgScanner.App.Services;

/// <summary>
/// Where the record editor's dividers were left, in device-independent pixels.
///
/// It used to carry a size per memo box as well. Memo boxes now fill the form pane and grow with
/// their text, so there is nothing per-box left to remember, and the divider is the width control
/// (SPEC-2026-005 amendment). A "memo" key in a layout stored by an older build is ignored.
/// </summary>
public sealed record RecordEditorLayout(double FormWidth, double TopHeight);

/// <summary>
/// Remembers the record editor's layout per group, over the Settings table (SPEC-2026-002 §07).
/// Nothing here touches WPF: the window measures, and this decides what a measurement may be.
/// </summary>
public sealed class RecordEditorLayoutStore(AppSettingsService settings, Serilog.ILogger? log = null)
{
    /// <summary>The starting point for a group the editor has not been opened on (§05 Q5).</summary>
    public const string LastKey = "RecordEditor.Layout.Last";

    /// <summary>The smallest a pane is restored at, so a divider dragged nearly shut can still be found and grabbed.</summary>
    public const double MinPane = 200;

    private readonly Serilog.ILogger _log = log ?? Serilog.Log.Logger;

    private readonly object _saveLock = new();

    private Task _lastSave = Task.CompletedTask;

    public static string KeyFor(Guid groupId) => $"RecordEditor.Layout.{groupId}";

    public async Task<RecordEditorLayout> LoadAsync(
        Guid groupId, double windowWidth, double windowHeight, CancellationToken cancellationToken = default)
    {
        var json = await settings.GetAsync(KeyFor(groupId), "", cancellationToken);
        if (json.Length == 0)
        {
            json = await settings.GetAsync(LastKey, "", cancellationToken);
        }

        var stored = json.Length == 0 ? null : Parse(json, groupId);
        return Clamp(stored ?? Defaults(windowWidth, windowHeight), windowWidth, windowHeight);
    }

    public async Task SaveAsync(Guid groupId, RecordEditorLayout layout, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new LayoutJson
        {
            FormWidth = layout.FormWidth,
            TopHeight = layout.TopHeight,
        });
        // One save at a time. A divider released while a memo drag is still saving has both writing
        // the shared "last layout" key; on its first ever use both find it missing, both add it, and
        // the loser fails on the duplicate — losing the size the operator had just dragged. Chained
        // rather than gated by a SemaphoreSlim, which the window would have to dispose, and closing
        // disposes it while the save it just started is still in flight.
        Task queued;
        lock (_saveLock)
        {
            _lastSave = WriteAfterAsync(_lastSave, groupId, json, cancellationToken);
            queued = _lastSave;
        }

        await queued;
    }

    /// <summary>The previous save's failure belongs to its own caller; this one still has to land.</summary>
    private async Task WriteAfterAsync(Task previous, Guid groupId, string json, CancellationToken cancellationToken)
    {
        await previous.ContinueWith(
            static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await settings.SetAsync(KeyFor(groupId), json, cancellationToken);
        await settings.SetAsync(LastKey, json, cancellationToken);
    }

    /// <summary>
    /// Fits a layout into a window. A layout saved on a large screen is read on the station's smaller
    /// one, and a pane pushed past the window edge takes its divider with it.
    /// </summary>
    public static RecordEditorLayout Clamp(RecordEditorLayout layout, double windowWidth, double windowHeight)
    {
        var formWidth = ClampPane(layout.FormWidth, windowWidth);
        return new RecordEditorLayout(formWidth, ClampPane(layout.TopHeight, windowHeight));
    }

    private static RecordEditorLayout Defaults(double windowWidth, double windowHeight) =>
        new(windowWidth / 2, windowHeight / 2);

    private static double ClampPane(double value, double window)
    {
        // A window too small for two minimum panes is split evenly rather than letting one vanish.
        if (window < 2 * MinPane)
        {
            return window / 2;
        }

        return double.IsFinite(value) ? Math.Clamp(value, MinPane, window - MinPane) : window / 2;
    }

    private RecordEditorLayout? Parse(string json, Guid groupId)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<LayoutJson>(json) ?? throw new JsonException("The layout is null.");
            // A missing number is NaN rather than 0, so Clamp gives it the default instead of the minimum.
            return new RecordEditorLayout(stored.FormWidth ?? double.NaN, stored.TopHeight ?? double.NaN);
        }
        catch (JsonException ex)
        {
            // Logged by group id only (§14); the next save rewrites the value.
            _log.Warning(ex, "The record editor layout for group {GroupId} could not be read; using the defaults", groupId);
            return null;
        }
    }

    private sealed class LayoutJson
    {
        [JsonPropertyName("formWidth")]
        public double? FormWidth { get; set; }

        [JsonPropertyName("topHeight")]
        public double? TopHeight { get; set; }

    }
}
