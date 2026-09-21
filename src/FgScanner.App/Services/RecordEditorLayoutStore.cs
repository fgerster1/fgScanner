using System.Text.Json;
using System.Text.Json.Serialization;
using FgScanner.Data;

namespace FgScanner.App.Services;

/// <summary>
/// Where the record editor's dividers and memo boxes were left. Pane sizes are device-independent
/// pixels; memo heights are lines of text, so a box keeps its meaning if the font size changes.
/// </summary>
public sealed record RecordEditorLayout(double FormWidth, double TopHeight, IReadOnlyDictionary<string, MemoSize> Memo);

/// <summary>A memo box's width in device-independent pixels and its height in lines.</summary>
public sealed record MemoSize(double Width, double Height);

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

    /// <summary>Mirror the memo TextBox's MinWidth in RecordEditorWindow.xaml: restoring anything narrower renders at that minimum and saves the wider number back, so a stored size would drift on its own.</summary>
    public const double MinMemoWidth = 120;

    public const double MinMemoLines = 2;

    public const double MaxMemoLines = 20;

    /// <summary>
    /// How tall a memo box opens before anybody has dragged one (SPEC-2026-005 §05 Q1a). Four
    /// lines: enough to read a stored note without resizing, which was the complaint — the box
    /// wrapped correctly but opened at about two lines, so every field needed dragging first.
    ///
    /// Fixed, deliberately NOT derived from the field's character limit: a field's width and its
    /// length are independent (ADR-0009), and that decision stands.
    /// </summary>
    public const double DefaultMemoLines = 4;

    /// <summary>The size a memo box opens at when nothing was stored for it: full pane, four lines.</summary>
    public static MemoSize DefaultMemo(double paneWidth) => ClampMemo(new MemoSize(paneWidth, DefaultMemoLines), paneWidth);

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
        var clamped = Clamp(stored ?? Defaults(windowWidth, windowHeight), windowWidth, windowHeight);

        // "My memo box keeps resizing itself" is otherwise guesswork: a size saved on a wider pane
        // is silently fitted to this one, and nothing said so.
        if (stored is not null)
        {
            foreach (var (name, size) in stored.Memo)
            {
                if (clamped.Memo.TryGetValue(name, out var fitted) && fitted != size)
                {
                    _log.Debug(
                        "Memo box {Field} restored at {Width}x{Height} lines instead of {StoredWidth}x{StoredHeight}",
                        name, fitted.Width, fitted.Height, size.Width, size.Height);
                }
            }
        }

        return clamped;
    }

    public async Task SaveAsync(Guid groupId, RecordEditorLayout layout, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new LayoutJson
        {
            FormWidth = layout.FormWidth,
            TopHeight = layout.TopHeight,
            Memo = layout.Memo.ToDictionary(m => m.Key, m => (MemoJson?)new MemoJson { Width = m.Value.Width, Height = m.Value.Height }),
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
        var memo = new Dictionary<string, MemoSize>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, size) in layout.Memo)
        {
            memo[name] = ClampMemo(size, formWidth);
        }

        return new RecordEditorLayout(formWidth, ClampPane(layout.TopHeight, windowHeight), memo);
    }

    /// <summary>A memo box may be as wide as the form pane and no wider (§08), and 2–20 lines tall.</summary>
    public static MemoSize ClampMemo(MemoSize size, double paneWidth)
    {
        paneWidth = double.IsFinite(paneWidth) ? Math.Max(0, paneWidth) : 0;
        var width = double.IsFinite(size.Width)
            ? Math.Clamp(size.Width, Math.Min(MinMemoWidth, paneWidth), paneWidth)
            : paneWidth;
        var height = double.IsFinite(size.Height) ? Math.Clamp(size.Height, MinMemoLines, MaxMemoLines) : MinMemoLines;
        return new MemoSize(width, height);
    }

    private static RecordEditorLayout Defaults(double windowWidth, double windowHeight) =>
        new(windowWidth / 2, windowHeight / 2, new Dictionary<string, MemoSize>());

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
            var memo = new Dictionary<string, MemoSize>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, size) in stored.Memo ?? [])
            {
                if (size is not null)
                {
                    memo[name] = new MemoSize(size.Width ?? double.NaN, size.Height ?? double.NaN);
                }
            }

            // A missing number is NaN rather than 0, so Clamp gives it the default instead of the minimum.
            return new RecordEditorLayout(stored.FormWidth ?? double.NaN, stored.TopHeight ?? double.NaN, memo);
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

        [JsonPropertyName("memo")]
        public Dictionary<string, MemoJson?>? Memo { get; set; }
    }

    private sealed class MemoJson
    {
        [JsonPropertyName("width")]
        public double? Width { get; set; }

        [JsonPropertyName("height")]
        public double? Height { get; set; }
    }
}
