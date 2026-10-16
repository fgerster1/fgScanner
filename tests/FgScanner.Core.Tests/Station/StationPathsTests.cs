using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// Franz processes Jim's station on his own NucBox (SPEC-SCANNER-2026-010 §03 item 1). The two
/// must never share a database, trash, recovery session or index drafts — a page Franz moves in
/// Jim's workspace landing in his own trash is evidence in the wrong custody.
/// </summary>
public class StationPathsTests
{
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void Without_a_data_dir_every_path_is_where_it_has_always_been()
    {
        var paths = StationPaths.Resolve(null);

        Assert.True(paths.IsDefault);
        Assert.Null(paths.Name);
        Assert.Equal(Path.Combine(AppData, "FGScanner"), paths.Root);
        Assert.Equal(Path.Combine(AppData, "FGScanner", "fgscanner.db"), paths.Database);
        Assert.Equal(Path.Combine(AppData, "FGScanner", "trash"), paths.Trash);
        Assert.Equal(Path.Combine(AppData, "FGScanner", "recovery"), paths.Recovery);
        Assert.Equal(Path.Combine(AppData, "FGScanner", "index-drafts"), paths.IndexDrafts);
        Assert.Equal(Path.Combine(AppData, "FGScanner", "tessdata"), paths.Tessdata);
        Assert.Equal(Path.Combine(LocalAppData, "FGScanner", "logs"), paths.Logs);
        Assert.Equal(Path.Combine(LocalAppData, "FGScanner", "index-packages"), paths.IndexPackages);
    }

    [Fact]
    public void A_data_dir_holds_everything_the_station_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "fg-station-jim");

        var paths = StationPaths.Resolve(root);

        Assert.False(paths.IsDefault);
        Assert.Equal("fg-station-jim", paths.Name);
        foreach (var path in new[]
                 {
                     paths.Database, paths.Trash, paths.Recovery, paths.IndexDrafts,
                     paths.Tessdata, paths.Logs, paths.IndexPackages,
                 })
        {
            Assert.StartsWith(root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_relative_data_dir_is_made_absolute_once()
    {
        var paths = StationPaths.Resolve("station-jim");

        Assert.True(Path.IsPathFullyQualified(paths.Root));
        Assert.Equal(Path.GetFullPath("station-jim"), paths.Root);
    }

    [Fact]
    public void The_default_folder_named_explicitly_is_still_the_default_station()
    {
        var paths = StationPaths.Resolve(Path.Combine(AppData, "FGScanner") + Path.DirectorySeparatorChar);

        Assert.True(paths.IsDefault);
        Assert.Equal(Path.Combine(LocalAppData, "FGScanner", "logs"), paths.Logs);
    }

    [Theory]
    [InlineData(new[] { "--data-dir", @"D:\Stations\Jim" }, @"D:\Stations\Jim")]
    [InlineData(new[] { "--fake-scanner", "--data-dir=D:\\Stations\\Jim" }, @"D:\Stations\Jim")]
    [InlineData(new[] { "--fake-scanner" }, null)]
    [InlineData(new string[0], null)]
    public void The_data_dir_is_read_from_the_command_line(string[] args, string? expected)
    {
        Assert.Equal(expected, StationPaths.DataDirFromArgs(args));
    }

    [Fact]
    public void A_data_dir_flag_with_no_folder_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StationPaths.DataDirFromArgs(["--data-dir"]));
        Assert.Throws<ArgumentException>(() => StationPaths.DataDirFromArgs(["--data-dir="]));
    }
}
