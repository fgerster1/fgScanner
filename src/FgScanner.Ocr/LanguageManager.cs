using System.Security.Cryptography;

namespace FgScanner.Ocr;

public sealed record OcrLanguage(string Code, string DisplayName, string Sha256);

/// <summary>
/// Manages the writable tessdata directory: copies the bundled eng there, and downloads other
/// tessdata_fast languages on demand with pinned SHA-256 verification (the tessdata_fast repo is
/// frozen upstream, so the hashes are stable).
/// </summary>
public sealed class LanguageManager(string? tessdataDir = null, HttpMessageHandler? httpHandler = null) : IDisposable
{
    private const string DownloadBaseUrl =
        "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/";

    /// <summary>Bundled English plus the curated downloadable set (hashes pinned 2026-08-20).</summary>
    public static readonly IReadOnlyList<OcrLanguage> KnownLanguages =
    [
        new("eng", "English", "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"),
        new("deu", "German", "19d219bbb6672c869d20a9636c6816a81eb9a71796cb93ebe0cb1530e2cdb22d"),
        new("fra", "French", "ced037562e8c80c13122dece28dd477d399af80911a28791a66a63ac1e3445ca"),
        new("spa", "Spanish", "6f2e04d02774a18f01bed44b1111f2cd7f3ba7ac9dc4373cd3f898a40ea6b464"),
        new("ita", "Italian", "b8f89e1e785118dac4d51ae042c029a64edb5c3ee42ef73027a6d412748d8827"),
        new("nld", "Dutch", "ced0e5e046a84c908a6aa7accbef9a232c4a5d9a8276691b81c6ee64d02963f6"),
        new("por", "Portuguese", "c4932b937207a9514b7514d518b931a99938c02a28a5a5a553f8599ed58b7deb"),
        new("pol", "Polish", "c4476cdbc0e33d898d32345122b7be1cbf85ace15f920f06c7714756e1ef79b2"),
        new("rus", "Russian", "e16e5e036cce1d9ec2b00063cf8b54472625b9e14d893a169e2b0dedeb4df225"),
    ];

    private readonly string _tessdataDir = tessdataDir ?? TesseractPaths.DefaultUserTessdataDir;
    private readonly HttpClient _http = new(httpHandler ?? new HttpClientHandler());

    public string TessdataDir => _tessdataDir;

    public bool IsInstalled(string code) =>
        File.Exists(Path.Combine(_tessdataDir, code + ".traineddata"));

    public IReadOnlyList<string> InstalledCodes() =>
        Directory.Exists(_tessdataDir)
            ? [.. Directory.EnumerateFiles(_tessdataDir, "*.traineddata")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(c => c is not null)
                .Select(c => c!)
                .OrderBy(c => c, StringComparer.Ordinal)]
            : [];

    /// <summary>Copies the shipped eng.traineddata into the writable dir so one folder holds all languages.</summary>
    public void EnsureBundledEnglish()
    {
        Directory.CreateDirectory(_tessdataDir);
        var target = Path.Combine(_tessdataDir, "eng.traineddata");
        var bundled = Path.Combine(TesseractPaths.BundledTessdataDir, "eng.traineddata");
        if (!File.Exists(target) && File.Exists(bundled))
        {
            File.Copy(bundled, target);
        }
    }

    public async Task InstallAsync(string code, CancellationToken cancellationToken = default)
    {
        var language = KnownLanguages.FirstOrDefault(l => l.Code == code)
            ?? throw new ArgumentException($"Unknown language code \"{code}\".", nameof(code));
        Directory.CreateDirectory(_tessdataDir);

        var bytes = await _http.GetByteArrayAsync(
            DownloadBaseUrl + code + ".traineddata", cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!actual.Equals(language.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Download of {code}.traineddata failed integrity check (got {actual}).");
        }

        var target = Path.Combine(_tessdataDir, code + ".traineddata");
        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
    }

    public void Dispose() => _http.Dispose();
}
