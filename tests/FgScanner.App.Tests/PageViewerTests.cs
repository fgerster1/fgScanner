using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The arithmetic behind the page viewer, kept out of the window so it can be checked without a
/// UI. Zoom limits and page navigation are exactly the parts that go wrong silently — an
/// off-by-one at the end of a group, or a scale that runs away on a fast scroll wheel.
/// </summary>
public sealed class ZoomControllerTests
{
    [Fact]
    public void Starts_at_actual_size()
    {
        Assert.Equal(1.0, new ZoomController().Scale);
    }

    [Fact]
    public void Zooming_in_and_back_out_returns_to_where_it_started()
    {
        var zoom = new ZoomController();

        zoom.In();
        zoom.Out();

        Assert.Equal(1.0, zoom.Scale, 6);
    }

    [Fact]
    public void Zooming_in_stops_at_the_maximum()
    {
        var zoom = new ZoomController();

        for (var i = 0; i < 100; i++)
        {
            zoom.In();
        }

        Assert.Equal(ZoomController.Maximum, zoom.Scale);
    }

    [Fact]
    public void Zooming_out_stops_at_the_minimum()
    {
        var zoom = new ZoomController();

        for (var i = 0; i < 100; i++)
        {
            zoom.Out();
        }

        Assert.Equal(ZoomController.Minimum, zoom.Scale);
    }

    [Fact]
    public void Fitting_a_tall_page_uses_the_dimension_that_runs_out_first()
    {
        var zoom = new ZoomController();

        // A 300-DPI portrait page in the short preview panel: height is the binding constraint,
        // and the scale it needs there is well below any comfortable-looking floor.
        zoom.Fit(contentWidth: 2550, contentHeight: 3300, viewportWidth: 1000, viewportHeight: 660, maxScale: 1);

        Assert.Equal(0.2, zoom.Scale, 6);
        Assert.True(zoom.Scale >= ZoomController.Minimum, "Fit must stay inside the zoom limits");
    }

    [Fact]
    public void Fitting_a_wide_page_uses_the_width()
    {
        var zoom = new ZoomController();

        zoom.Fit(contentWidth: 2000, contentHeight: 500, viewportWidth: 500, viewportHeight: 1000, maxScale: 1);

        Assert.Equal(0.25, zoom.Scale, 6);
    }

    [Fact]
    public void Fit_uses_the_layout_size_so_a_tall_page_fills_the_height()
    {
        // A 300-DPI letter page lays out at 816x1056 units. Fed its 2550x3300 pixel count instead,
        // the same viewer showed it 224 units tall of 700.
        var zoom = new ZoomController();

        zoom.Fit(contentWidth: 816, contentHeight: 1056, viewportWidth: 950, viewportHeight: 700, maxScale: 3.125);

        Assert.Equal(700.0 / 1056.0, zoom.Scale, 6);
    }

    /// <summary>
    /// Replaces "never enlarges": that cap was 1.0 in layout units, which is a third of paper size for
    /// a 300-DPI page. The real limit is where one image pixel would cover more than one screen pixel.
    /// </summary>
    [Theory]
    [InlineData(3.125, 3.125)]
    [InlineData(1.0, 1.0)]
    public void Fit_stops_where_image_pixels_would_be_magnified(double maxScale, double expected)
    {
        var zoom = new ZoomController();

        zoom.Fit(contentWidth: 200, contentHeight: 200, viewportWidth: 1000, viewportHeight: 1000, maxScale);

        Assert.Equal(expected, zoom.Scale, 6);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 100)]
    public void Fitting_against_a_viewport_with_no_size_leaves_the_scale_alone(double width, double height)
    {
        // A window still being laid out reports zero. Dividing by it would produce infinity and
        // hand the layout an image of unbounded size.
        var zoom = new ZoomController();
        zoom.In();
        var before = zoom.Scale;

        zoom.Fit(2550, 3300, width, height, maxScale: 1);

        Assert.Equal(before, zoom.Scale);
    }

    [Fact]
    public void Resetting_returns_to_actual_size()
    {
        var zoom = new ZoomController();
        zoom.In();
        zoom.In();

        zoom.Reset();

        Assert.Equal(1.0, zoom.Scale);
    }
}

/// <summary>
/// How big a page image lays out. Stretch="None" draws an image at its DPI-scaled size, so Fit fed
/// the pixel count instead rendered a 300-DPI scan 224 units tall in a 700-unit viewer.
/// </summary>
public sealed class ImageLayoutTests
{
    [Fact]
    public void A_300_dpi_scan_lays_out_at_paper_size_not_pixel_count()
    {
        var layout = ImageLayout.Of(Blank(pixelWidth: 2550, pixelHeight: 3300, dpi: 300));

        Assert.Equal(816, layout.Width, 6);
        Assert.Equal(1056, layout.Height, 6);
        Assert.Equal(3.125, layout.MaxScale, 6);
    }

    [Fact]
    public void An_image_at_96_dpi_lays_out_at_its_pixel_size()
    {
        // Screenshots and files without DPI metadata: one pixel per unit, so Fit never enlarges them.
        var layout = ImageLayout.Of(Blank(pixelWidth: 1200, pixelHeight: 800, dpi: 96));

        Assert.Equal(1200, layout.Width, 6);
        Assert.Equal(800, layout.Height, 6);
        Assert.Equal(1.0, layout.MaxScale, 6);
    }

    private static BitmapSource Blank(int pixelWidth, int pixelHeight, double dpi) =>
        BitmapSource.Create(
            pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Gray8, null,
            new byte[pixelWidth * pixelHeight], pixelWidth);
}

/// <summary>
/// When a page re-fits by itself. A fit that ignores resizing looks broken the moment a divider is
/// dragged; one that re-fits after the user zoomed throws their zoom away.
/// </summary>
public sealed class FitPolicyTests
{
    // A 300-DPI portrait page: 816x1056 layout units, 3.125 image pixels per unit.
    private const double PageWidth = 816;
    private const double PageHeight = 1056;
    private const double PixelsPerUnit = 3.125;

    [Fact]
    public void Resizing_refits_while_the_user_has_not_zoomed()
    {
        var zoom = new ZoomController();
        var policy = new FitPolicy(zoom);
        policy.Fit(PageWidth, PageHeight, 950, 700, PixelsPerUnit);

        policy.ViewportResized(PageWidth, PageHeight, 950, 350, PixelsPerUnit);

        Assert.Equal(350.0 / 1056.0, zoom.Scale, 6);
    }

    [Fact]
    public void Resizing_keeps_a_zoom_the_user_chose()
    {
        var zoom = new ZoomController();
        var policy = new FitPolicy(zoom);
        policy.Fit(PageWidth, PageHeight, 950, 700, PixelsPerUnit);
        policy.In();
        var chosen = zoom.Scale;

        policy.ViewportResized(PageWidth, PageHeight, 950, 350, PixelsPerUnit);

        Assert.Equal(chosen, zoom.Scale, 6);
    }

    [Fact]
    public void Pressing_fit_after_a_manual_zoom_resumes_refitting()
    {
        var zoom = new ZoomController();
        var policy = new FitPolicy(zoom);
        policy.Out();
        policy.Fit(PageWidth, PageHeight, 950, 700, PixelsPerUnit);

        policy.ViewportResized(PageWidth, PageHeight, 950, 350, PixelsPerUnit);

        Assert.Equal(350.0 / 1056.0, zoom.Scale, 6);
    }

    [Fact]
    public void Actual_size_counts_as_a_zoom_the_user_chose()
    {
        var zoom = new ZoomController();
        var policy = new FitPolicy(zoom);
        policy.Fit(PageWidth, PageHeight, 950, 700, PixelsPerUnit);
        policy.Reset();

        policy.ViewportResized(PageWidth, PageHeight, 950, 350, PixelsPerUnit);

        Assert.Equal(1.0, zoom.Scale);
    }

    [Fact]
    public void A_fit_asked_for_before_layout_happens_on_the_first_usable_size()
    {
        // A window still being laid out reports a zero viewport; the page must not open at 1:1.
        var zoom = new ZoomController();
        var policy = new FitPolicy(zoom);
        policy.Fit(PageWidth, PageHeight, 0, 0, PixelsPerUnit);

        policy.ViewportResized(PageWidth, PageHeight, 950, 700, PixelsPerUnit);

        Assert.Equal(700.0 / 1056.0, zoom.Scale, 6);
    }
}

public sealed class PageNavigatorTests
{
    [Fact]
    public void Opens_on_the_page_that_was_double_clicked()
    {
        Assert.Equal(2, new PageNavigator(5, 2).Index);
    }

    [Fact]
    public void Next_advances_one_page()
    {
        var nav = new PageNavigator(5, 0);

        nav.Next();

        Assert.Equal(1, nav.Index);
    }

    [Fact]
    public void Next_stops_at_the_last_page()
    {
        var nav = new PageNavigator(3, 2);

        nav.Next();

        Assert.Equal(2, nav.Index);
        Assert.False(nav.CanGoNext);
    }

    [Fact]
    public void Previous_stops_at_the_first_page()
    {
        var nav = new PageNavigator(3, 0);

        nav.Previous();

        Assert.Equal(0, nav.Index);
        Assert.False(nav.CanGoPrevious);
    }

    [Fact]
    public void First_and_last_jump_to_the_ends()
    {
        var nav = new PageNavigator(10, 4);

        nav.Last();
        Assert.Equal(9, nav.Index);

        nav.First();
        Assert.Equal(0, nav.Index);
    }

    [Fact]
    public void A_single_page_group_can_go_nowhere()
    {
        var nav = new PageNavigator(1, 0);

        Assert.False(nav.CanGoNext);
        Assert.False(nav.CanGoPrevious);
    }

    [Fact]
    public void The_position_reads_from_one_not_zero()
    {
        Assert.Equal("Page 3 of 7", new PageNavigator(7, 2).Position);
    }

    [Theory]
    [InlineData(5, -1, 0)]
    [InlineData(5, 99, 4)]
    public void A_start_index_outside_the_group_is_pulled_back_inside(int count, int start, int expected)
    {
        // The grid can be re-sorted or a row deleted between opening and rendering.
        Assert.Equal(expected, new PageNavigator(count, start).Index);
    }

    [Fact]
    public void An_empty_group_reports_no_position_rather_than_page_one_of_zero()
    {
        var nav = new PageNavigator(0, 0);

        Assert.Equal("", nav.Position);
        Assert.False(nav.CanGoNext);
        Assert.False(nav.CanGoPrevious);
    }
}

/// <summary>
/// The viewer takes image paths so that Scan's unsaved pages can use it too. Groups must still land
/// its grid on the page the viewer closed on, or closing on page 7 drops the user back on page 1.
/// </summary>
public sealed class GroupPageViewerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public GroupPageViewerTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        var factory = new TestFactory(_dbPath);
        _groupService = new GroupService(factory);
        _profileService = new ProfileService(factory);
        _indexingService = new IndexingService(factory, _profileService, new IndexExporter());
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
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

    private PageEditingToolset CreateToolset() => new(
        new FgScanner.Scanning.Editing.ImageEditor(),
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        new FgScanner.Scanning.Import.FileImportService(),
        new ReorderService(new TestFactory(_dbPath)),
        new OcrQueueService(new TestFactory(_dbPath)),
        new AiQueueService(new TestFactory(_dbPath)),
        new RetroProcessService(new TestFactory(_dbPath), _groupService, _trashService),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        new AppSettingsService(new TestFactory(_dbPath)),
        new CaptureTriageService(new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath))),
        new DuplicateFinder(new TestFactory(_dbPath)));

    [Fact]
    public async Task Grid_follows_the_viewers_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await _groupService.CreateGroupAsync(_root, "Viewer", null, ct);
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var files = new List<string>();
        for (byte i = 1; i <= 3; i++)
        {
            var file = Path.Combine(staging, $"scan_0000{i}.png");
            await File.WriteAllBytesAsync(file, [i, i, i], ct);
            files.Add(file);
        }

        await _groupService.AdoptPagesAsync(group.Id, files, _ => false, ct);
        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, new ActiveGroupStore(),
            CreateToolset());
        await vm.LoadAsync();
        vm.SelectedRow = vm.Rows[0];
        IReadOnlyList<string>? shown = null;
        var shownStart = -1;
        vm.ShowPageViewer = (paths, start) =>
        {
            shown = paths;
            shownStart = start;
            return 2;
        };

        vm.OpenPageViewerCommand.Execute(null);

        Assert.Equal(vm.Rows.Select(r => r.ImagePath), shown);
        Assert.Equal(0, shownStart);
        Assert.Same(vm.Rows[2], vm.SelectedRow);
    }
}
