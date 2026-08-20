using FgScanner.Core;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class GroupServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _service;
    private readonly string _groupsRoot;
    private readonly string _incoming;

    public GroupServiceTests()
    {
        _service = new GroupService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        _incoming = Path.Combine(_db.Root, "incoming");
        Directory.CreateDirectory(_groupsRoot);
        Directory.CreateDirectory(_incoming);
    }

    public void Dispose() => _db.Dispose();

    private string NewIncomingFile(string name, byte[] content)
    {
        var path = Path.Combine(_incoming, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task Create_group_makes_directory_named_after_sanitized_name()
    {
        var group = await _service.CreateGroupAsync(_groupsRoot, "Q1: Invoices?", TestContext.Current.CancellationToken);

        Assert.Equal("Q1- Invoices", group.Name);
        Assert.True(Directory.Exists(group.DirectoryPath));
    }

    [Fact]
    public async Task Adopting_the_same_directory_twice_returns_the_same_group()
    {
        var dir = Path.Combine(_groupsRoot, "Receipts");
        var first = await _service.AdoptDirectoryAsync(dir, TestContext.Current.CancellationToken);
        var second = await _service.AdoptDirectoryAsync(dir, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Adopt_pages_moves_files_and_creates_one_document_per_page()
    {
        var group = await _service.CreateGroupAsync(_groupsRoot, "Batch", TestContext.Current.CancellationToken);
        var a = NewIncomingFile("page-00001.png", [1, 2, 3]);
        var b = NewIncomingFile("page-00002.png", [4, 5, 6]);

        var result = await _service.AdoptPagesAsync(group.Id, [a, b], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Adopted.Count);
        Assert.Empty(result.DuplicateSourceFiles);
        Assert.Equal(["scan_00001.png", "scan_00002.png"], result.Adopted.Select(p => p.FileName));
        Assert.False(File.Exists(a)); // moved, not copied
        Assert.True(File.Exists(Path.Combine(group.DirectoryPath, "scan_00001.png")));

        var pages = await _service.GetPagesAsync(group.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.Equal(64, p.Checksum.Length));
    }

    [Fact]
    public async Task Duplicate_content_is_skipped_and_reported()
    {
        var group = await _service.CreateGroupAsync(_groupsRoot, "Dedupe", TestContext.Current.CancellationToken);
        var original = NewIncomingFile("first.png", [9, 9, 9]);
        await _service.AdoptPagesAsync(group.Id, [original], TestContext.Current.CancellationToken);
        var duplicate = NewIncomingFile("second.png", [9, 9, 9]); // same bytes, different name

        var result = await _service.AdoptPagesAsync(group.Id, [duplicate], TestContext.Current.CancellationToken);

        Assert.Empty(result.Adopted);
        Assert.Equal([duplicate], result.DuplicateSourceFiles);
        Assert.Single(await _service.GetPagesAsync(group.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Later_adoption_continues_the_sequence()
    {
        var group = await _service.CreateGroupAsync(_groupsRoot, "Append", TestContext.Current.CancellationToken);
        await _service.AdoptPagesAsync(
            group.Id, [NewIncomingFile("a.png", [1])], TestContext.Current.CancellationToken);

        var result = await _service.AdoptPagesAsync(
            group.Id, [NewIncomingFile("b.png", [2])], TestContext.Current.CancellationToken);

        Assert.Equal("scan_00002.png", Assert.Single(result.Adopted).FileName);
    }

    [Theory]
    [InlineData("Q1: Invoices?", "Q1- Invoices")]
    [InlineData("CON", "_CON")]
    [InlineData("com1.backup", "_com1.backup")]
    [InlineData("Scans 2025.", "Scans 2025")]
    [InlineData("  ", "Group")]
    [InlineData("a<b>c|d", "a-b-c-d")]
    public void Sanitizer_handles_windows_reserved_names_and_chars(string input, string expected) =>
        Assert.Equal(expected, GroupNameSanitizer.Sanitize(input));
}
