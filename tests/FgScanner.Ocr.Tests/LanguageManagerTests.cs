using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace FgScanner.Ocr.Tests;

public sealed class LanguageManagerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-lang").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Serves canned bytes for any URL — downloads never hit the network in tests.</summary>
    private sealed class FakeHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
    }

    [Fact]
    public void Bundled_english_is_copied_into_the_writable_dir()
    {
        using var manager = new LanguageManager(Path.Combine(_dir, "td"));

        manager.EnsureBundledData();

        Assert.True(manager.IsInstalled("eng"));
        Assert.Equal(["eng"], manager.InstalledCodes());
    }

    [Fact]
    public async Task Download_with_matching_hash_installs_the_language()
    {
        // Serve the real bundled eng bytes but install them under a language whose pinned hash
        // matches — impossible for a fake payload, so pin-check is exercised with a crafted list?
        // No: the pinned hash for eng matches the bundled file, so serve those bytes as "eng".
        var payload = await File.ReadAllBytesAsync(
            Path.Combine(TesseractPaths.BundledTessdataDir, "eng.traineddata"), Ct);
        var expected = Convert.ToHexStringLower(SHA256.HashData(payload));
        Assert.Equal(
            LanguageManager.KnownLanguages.First(l => l.Code == "eng").Sha256,
            expected); // guards the pinned hash against a stale bundled file
        using var manager = new LanguageManager(Path.Combine(_dir, "td2"), new FakeHandler(payload));

        await manager.InstallAsync("eng", Ct);

        Assert.True(manager.IsInstalled("eng"));
    }

    [Fact]
    public async Task Corrupted_download_is_rejected_and_not_installed()
    {
        using var manager = new LanguageManager(
            Path.Combine(_dir, "td3"), new FakeHandler([1, 2, 3, 4]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallAsync("deu", Ct));

        Assert.False(manager.IsInstalled("deu"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "td3")));
    }

    [Fact]
    public async Task Unknown_language_code_is_refused()
    {
        using var manager = new LanguageManager(Path.Combine(_dir, "td4"), new FakeHandler([1]));

        await Assert.ThrowsAsync<ArgumentException>(() => manager.InstallAsync("xyz", Ct));
    }
}
