using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace FgScanner.App.Tests;

public sealed class RecordEditorLayoutStoreTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, MemoSize> NoMemo = new Dictionary<string, MemoSize>();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly AppSettingsService _settings;
    private readonly CapturingSink _log = new();
    private readonly RecordEditorLayoutStore _store;

    public RecordEditorLayoutStoreTests()
    {
        Directory.CreateDirectory(_root);
        var dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(dbPath)))
        {
            db.Database.Migrate();
        }

        _settings = new AppSettingsService(new TestFactory(dbPath));
        _store = new RecordEditorLayoutStore(_settings, new LoggerConfiguration().WriteTo.Sink(_log).CreateLogger());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }

    [Fact]
    public async Task Saves_under_the_groups_own_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();

        await _store.SaveAsync(
            groupId,
            new RecordEditorLayout(500, 400, new Dictionary<string, MemoSize> { ["Notes"] = new(300, 6) }),
            ct);

        var raw = await _settings.GetAsync($"RecordEditor.Layout.{groupId}", "", ct);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(500, json.RootElement.GetProperty("formWidth").GetDouble());
        Assert.Equal(400, json.RootElement.GetProperty("topHeight").GetDouble());
        var notes = json.RootElement.GetProperty("memo").GetProperty("Notes");
        Assert.Equal(300, notes.GetProperty("width").GetDouble());
        Assert.Equal(6, notes.GetProperty("height").GetDouble());
    }

    [Fact]
    public async Task A_group_not_seen_before_starts_from_the_last_layout()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(Guid.NewGuid(), new RecordEditorLayout(520, 410, NoMemo), ct);

        var layout = await _store.LoadAsync(Guid.NewGuid(), 1600, 1000, ct);

        Assert.Equal(520, layout.FormWidth);
        Assert.Equal(410, layout.TopHeight);
    }

    [Fact]
    public async Task Each_group_keeps_its_own_layout()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Guid.NewGuid();
        await _store.SaveAsync(first, new RecordEditorLayout(520, 410, NoMemo), ct);
        await _store.SaveAsync(Guid.NewGuid(), new RecordEditorLayout(700, 300, NoMemo), ct);

        var layout = await _store.LoadAsync(first, 1600, 1000, ct);

        Assert.Equal(520, layout.FormWidth);
        Assert.Equal(410, layout.TopHeight);
    }

    [Fact]
    public async Task Nothing_saved_splits_the_window_evenly()
    {
        var layout = await _store.LoadAsync(Guid.NewGuid(), 1200, 800, TestContext.Current.CancellationToken);

        Assert.Equal(600, layout.FormWidth);
        Assert.Equal(400, layout.TopHeight);
        Assert.Empty(layout.Memo);
    }

    /// <summary>The station's screen is smaller than the dev machine's; a layout saved on one must not push a pane off the other.</summary>
    [Fact]
    public async Task A_layout_larger_than_the_window_is_clamped()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(groupId, new RecordEditorLayout(2400, 1800, NoMemo), ct);

        var layout = await _store.LoadAsync(groupId, 1280, 1024, ct);

        Assert.Equal(1280 - RecordEditorLayoutStore.MinPane, layout.FormWidth);
        Assert.Equal(1024 - RecordEditorLayoutStore.MinPane, layout.TopHeight);
    }

    [Fact]
    public async Task A_pane_dragged_nearly_shut_is_restored_at_the_minimum()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(groupId, new RecordEditorLayout(10, 10, NoMemo), ct);

        var layout = await _store.LoadAsync(groupId, 1280, 1024, ct);

        Assert.Equal(RecordEditorLayoutStore.MinPane, layout.FormWidth);
        Assert.Equal(RecordEditorLayoutStore.MinPane, layout.TopHeight);
    }

    [Fact]
    public async Task Corrupt_json_gives_the_defaults_and_logs_a_warning_without_the_contents()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _settings.SetAsync($"RecordEditor.Layout.{groupId}", "{not json", ct);

        var layout = await _store.LoadAsync(groupId, 1200, 800, ct);

        Assert.Equal(600, layout.FormWidth);
        Assert.Equal(400, layout.TopHeight);
        Assert.Empty(layout.Memo);
        var warning = Assert.Single(_log.Events, e => e.Level == LogEventLevel.Warning);
        var message = warning.RenderMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(groupId.ToString(), message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not json", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Memo_sizes_are_kept_by_field_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(
            groupId,
            new RecordEditorLayout(
                600,
                400,
                new Dictionary<string, MemoSize> { ["Notes"] = new(300, 6), ["Title"] = new(250, 3) }),
            ct);

        var layout = await _store.LoadAsync(groupId, 1600, 1000, ct);

        Assert.Equal(new MemoSize(300, 6), layout.Memo["Notes"]);
        Assert.Equal(new MemoSize(250, 3), layout.Memo["Title"]);
    }

    /// <summary>
    /// A divider released while another save is still in flight: both write the shared "last layout"
    /// key, and on its first ever use both see it missing and try to add it. The loser used to take
    /// the whole save down, so the size the operator had just dragged was silently not remembered.
    /// </summary>
    [Fact]
    public async Task Two_saves_at_once_both_land()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await Task.WhenAll(
            _store.SaveAsync(first, new RecordEditorLayout(520, 410, NoMemo), ct),
            _store.SaveAsync(second, new RecordEditorLayout(640, 300, NoMemo), ct));

        Assert.Equal(520, (await _store.LoadAsync(first, 1600, 1000, ct)).FormWidth);
        Assert.Equal(640, (await _store.LoadAsync(second, 1600, 1000, ct)).FormWidth);
        Assert.DoesNotContain(_log.Events, e => e.Level >= LogEventLevel.Warning);
    }

    /// <summary>
    /// The box on screen has a minimum width of its own. Restoring anything narrower renders at that
    /// minimum and the next save writes the wider number back, so a stored size would drift on its own.
    /// </summary>
    [Fact]
    public void A_memo_box_is_never_narrower_than_the_box_on_screen()
    {
        // 120 is the memo TextBox's MinWidth in RecordEditorWindow.xaml; the two must agree.
        Assert.Equal(120, RecordEditorLayoutStore.ClampMemo(new MemoSize(10, 6), paneWidth: 400).Width);
    }

    [Fact]
    public void A_memo_box_is_never_wider_than_its_pane()
    {
        Assert.Equal(400, RecordEditorLayoutStore.ClampMemo(new MemoSize(900, 6), paneWidth: 400).Width);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(6, 6)]
    [InlineData(25, 20)]
    public void A_memo_box_is_between_2_and_20_lines_tall(double lines, double expected)
    {
        Assert.Equal(expected, RecordEditorLayoutStore.ClampMemo(new MemoSize(300, lines), paneWidth: 400).Height);
    }

    [Fact]
    public async Task A_saved_memo_wider_than_the_restored_form_pane_is_clamped_to_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(
            groupId,
            new RecordEditorLayout(2400, 600, new Dictionary<string, MemoSize> { ["Notes"] = new(2000, 6) }),
            ct);

        var layout = await _store.LoadAsync(groupId, 1280, 1024, ct);

        Assert.Equal(layout.FormWidth, layout.Memo["Notes"].Width);
    }
}
