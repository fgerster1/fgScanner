using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Source list offered every ScanSource whatever the scanner could do, under its bare enum
/// name. A flatbed-only device set to Duplex reached NAPS2's NoDuplexSupportException and the
/// operator was shown a raw driver message (SPEC-2026-006 §04, AC-1). The scanner is asked once,
/// when a device is chosen, and an unsupported source stays visible but disabled with the reason
/// beside it — §05 N1 answer (a): hiding it makes the operator hunt for a feature they can see
/// on the box.
/// </summary>
public sealed class DuplexOptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public DuplexOptionTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _sessionService = new ScanSessionService(Path.Combine(_root, "recovery"));
        var factory = new TestFactory(_dbPath);
        _groupService = new GroupService(factory);
        _profileService = new ProfileService(factory);
        _indexingService = new IndexingService(factory, _profileService, new FgScanner.Core.Index.IndexExporter());
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        _sessionService.Dispose();
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

    private RetroProcessService CreateRetroService() => new(
        new TestFactory(_dbPath), _groupService, _trashService);

    private CaptureTriageService CreateTriageService() => new(
        new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath)));

    private PageEditingToolset CreateToolset() => new(
        new FgScanner.Scanning.Editing.ImageEditor(),
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        new FgScanner.Scanning.Import.FileImportService(),
        new ReorderService(new TestFactory(_dbPath)),
        new OcrQueueService(new TestFactory(_dbPath)),
        new AiQueueService(new TestFactory(_dbPath)),
        CreateRetroService(),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        new AppSettingsService(new TestFactory(_dbPath)),
        CreateTriageService(),
        new DuplicateFinder(new TestFactory(_dbPath)));

    private ScanViewModel CreateScanViewModel(IScanService scanner) => new(
        scanner, _sessionService, _groupService, _indexingService, _activeGroup,
        new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
        CreateToolset(), _trashService);

    private static SourceOption OptionFor(ScanViewModel scan, ScanSource source) =>
        scan.Sources.Single(s => s.Source == source);

    [Fact]
    public async Task An_unsupported_device_disables_the_duplex_source()
    {
        var scan = CreateScanViewModel(new FakeScanService
        {
            Capabilities = new ScanCapabilities(Flatbed: true, Feeder: true, Duplex: false),
        });

        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        var duplex = OptionFor(scan, ScanSource.Duplex);
        Assert.False(duplex.IsSupported);
        Assert.False(string.IsNullOrWhiteSpace(duplex.Reason));
    }

    [Fact]
    public async Task A_device_that_reports_duplex_leaves_it_selectable()
    {
        var scan = CreateScanViewModel(new FakeScanService());

        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.True(OptionFor(scan, ScanSource.Duplex).IsSupported);
        Assert.All(scan.Sources, option => Assert.True(option.IsSupported));
    }

    /// <summary>
    /// A feeder-only device is the common case on this station, and "Feeder" alone does not say
    /// which of the two feeder entries scans both sides.
    /// </summary>
    [Fact]
    public async Task The_sources_read_as_words_not_enum_names()
    {
        var scan = CreateScanViewModel(new FakeScanService());

        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Equal("Flatbed", OptionFor(scan, ScanSource.Flatbed).Name);
        Assert.Equal("Feeder (one side)", OptionFor(scan, ScanSource.Feeder).Name);
        Assert.Equal("Feeder (both sides, one pass)", OptionFor(scan, ScanSource.Duplex).Name);
    }

    /// <summary>
    /// §16 R5: the probe is a convenience, never a gate. A driver that cannot answer — or answers
    /// wrongly, which some TWAIN drivers do — must not be able to stop a scan the hardware can
    /// actually do. Everything is offered and the scan itself reports what happens.
    /// </summary>
    [Fact]
    public async Task A_scanner_that_cannot_answer_is_offered_everything()
    {
        var scan = CreateScanViewModel(new FakeScanService
        {
            CapabilitiesError = new InvalidOperationException("driver does not implement capabilities"),
        });

        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.All(scan.Sources, option => Assert.True(option.IsSupported));
    }

    /// <summary>The answer belongs to the device that gave it, not to the one selected before it.</summary>
    [Fact]
    public async Task Choosing_another_device_asks_that_device()
    {
        var scanner = new FakeScanService
        {
            Capabilities = new ScanCapabilities(Flatbed: true, Feeder: false, Duplex: false),
        };
        var scan = CreateScanViewModel(scanner);
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.False(OptionFor(scan, ScanSource.Duplex).IsSupported);

        // The fake offers one device per driver, so moving to another scanner means moving driver —
        // which is how the operator reaches the second machine on this station anyway.
        scanner.Capabilities = ScanCapabilities.Everything;
        scan.SelectedDriver = ScanDriver.Twain;
        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Equal("fake-twain-1", scan.SelectedDevice?.Id);
        Assert.True(OptionFor(scan, ScanSource.Duplex).IsSupported);
    }

    /// <summary>
    /// The driver's own words for this are "NoDuplexSupportException", which told the operator
    /// nothing about what to do next. A scanner that cannot do both sides at once can still do
    /// both sides in two passes, and the message has to say so.
    /// </summary>
    [Fact]
    public async Task A_scanner_that_cannot_do_duplex_says_what_to_do_instead()
    {
        var scan = CreateScanViewModel(new FakeScanService
        {
            Error = new ScanSourceUnavailableException(ScanSource.Duplex),
        });
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        scan.Source = ScanSource.Duplex;

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.Contains("both sides", scan.StatusText, StringComparison.OrdinalIgnoreCase);

        // The distinguishing assertion: the generic handler prefixes "Scan failed:", which reads
        // as a fault when this is an instruction the operator can act on. Without the named catch
        // the line above passes and this one does not.
        Assert.DoesNotContain("Scan failed", scan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_feeder_says_the_feeder_is_empty()
    {
        var scan = CreateScanViewModel(new FakeScanService { Error = new FeederEmptyException() });
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        scan.Source = ScanSource.Feeder;

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.Contains("feeder", scan.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scan failed", scan.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The flip checkbox corrects upside-down backs from a one-pass duplex scanner, so it means
    /// nothing on a source that only ever scans one side.
    /// </summary>
    [Fact]
    public async Task Flipping_backs_is_offered_only_for_one_pass_duplex()
    {
        var scan = CreateScanViewModel(new FakeScanService());
        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        scan.Source = ScanSource.Feeder;
        Assert.False(scan.CanFlipDuplexedPages);

        scan.Source = ScanSource.Duplex;
        Assert.True(scan.CanFlipDuplexedPages);
    }

    /// <summary>
    /// The probe runs when a device is chosen and never on the scan path: asking the driver again
    /// mid-run is a round trip to the hardware for an answer that cannot have changed.
    /// </summary>
    [Fact]
    public async Task The_scanner_is_asked_once_per_device_not_once_per_scan()
    {
        var scanner = new FakeScanService();
        var scan = CreateScanViewModel(scanner);
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        var afterSelection = scanner.CapabilityProbes;

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, afterSelection);
        Assert.Equal(afterSelection, scanner.CapabilityProbes);
    }
}
