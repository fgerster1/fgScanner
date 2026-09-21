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
            new RecordEditorLayout(500, 400),
            ct);

        var raw = await _settings.GetAsync($"RecordEditor.Layout.{groupId}", "", ct);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(500, json.RootElement.GetProperty("formWidth").GetDouble());
        Assert.Equal(400, json.RootElement.GetProperty("topHeight").GetDouble());
    }

    /// <summary>
    /// Memo boxes were once sized one by one and their sizes stored here. They now fill the form
    /// pane and grow with their text, so a layout is the two dividers and nothing else — and a
    /// layout written by an older build, which does carry memo sizes, still has to load.
    /// </summary>
    [Fact]
    public async Task A_saved_layout_carries_pane_sizes_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _settings.SetAsync(
            $"RecordEditor.Layout.{groupId}",
            """{"formWidth":480,"topHeight":360,"memo":{"Notes":{"width":300,"height":6}}}""",
            ct);

        var layout = await _store.LoadAsync(groupId, 1600, 1000, ct);

        Assert.Equal(480, layout.FormWidth);
        Assert.Equal(360, layout.TopHeight);
        await _store.SaveAsync(groupId, layout, ct);
        var written = await _settings.GetAsync($"RecordEditor.Layout.{groupId}", "", ct);
        Assert.DoesNotContain("memo", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_group_not_seen_before_starts_from_the_last_layout()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(Guid.NewGuid(), new RecordEditorLayout(520, 410), ct);

        var layout = await _store.LoadAsync(Guid.NewGuid(), 1600, 1000, ct);

        Assert.Equal(520, layout.FormWidth);
        Assert.Equal(410, layout.TopHeight);
    }

    [Fact]
    public async Task Each_group_keeps_its_own_layout()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Guid.NewGuid();
        await _store.SaveAsync(first, new RecordEditorLayout(520, 410), ct);
        await _store.SaveAsync(Guid.NewGuid(), new RecordEditorLayout(700, 300), ct);

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
    }

    /// <summary>The station's screen is smaller than the dev machine's; a layout saved on one must not push a pane off the other.</summary>
    [Fact]
    public async Task A_layout_larger_than_the_window_is_clamped()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(groupId, new RecordEditorLayout(2400, 1800), ct);

        var layout = await _store.LoadAsync(groupId, 1280, 1024, ct);

        Assert.Equal(1280 - RecordEditorLayoutStore.MinPane, layout.FormWidth);
        Assert.Equal(1024 - RecordEditorLayoutStore.MinPane, layout.TopHeight);
    }

    [Fact]
    public async Task A_pane_dragged_nearly_shut_is_restored_at_the_minimum()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        await _store.SaveAsync(groupId, new RecordEditorLayout(10, 10), ct);

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
        var warning = Assert.Single(_log.Events, e => e.Level == LogEventLevel.Warning);
        var message = warning.RenderMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(groupId.ToString(), message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not json", message, StringComparison.Ordinal);
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
            _store.SaveAsync(first, new RecordEditorLayout(520, 410), ct),
            _store.SaveAsync(second, new RecordEditorLayout(640, 300), ct));

        Assert.Equal(520, (await _store.LoadAsync(first, 1600, 1000, ct)).FormWidth);
        Assert.Equal(640, (await _store.LoadAsync(second, 1600, 1000, ct)).FormWidth);
        Assert.DoesNotContain(_log.Events, e => e.Level >= LogEventLevel.Warning);
    }






}
