namespace FgScanner.Core.Imaging;

/// <summary>
/// Naming for the untouched-capture archive (Feature.PreserveOriginals). The pre-edit bytes of
/// a page live at originals\&lt;same filename&gt; inside its group folder, so the archive travels
/// with the folder and an evidence importer can verify it without this application present.
/// </summary>
public static class OriginalArchive
{
    public const string FolderName = "originals";

    /// <summary>The archive path for a page image, in the same directory the image lives in.</summary>
    public static string PathFor(string imagePath) =>
        Path.Combine(
            Path.GetDirectoryName(imagePath) ?? "",
            FolderName,
            Path.GetFileName(imagePath));
}
