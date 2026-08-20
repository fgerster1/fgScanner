using System.Globalization;

namespace FgScanner.Scanning.Tests;

/// <summary>Minimal IPageStorage over a plain folder for import/export tests.</summary>
internal sealed class FakePageStorage(string root) : IPageStorage
{
    private int _next;

    public List<ScannedPage> Committed { get; } = [];

    public string ReserveNextPagePath(string extension)
    {
        Directory.CreateDirectory(root);
        _next++;
        return Path.Combine(
            root, $"page-{_next.ToString("00000", CultureInfo.InvariantCulture)}.{extension}");
    }

    public void CommitPage(ScannedPage page) => Committed.Add(page);
}
