using System.Runtime.InteropServices;

namespace FgScanner.Ocr;

/// <summary>Locates the tesseract.exe that NAPS2.Tesseract.Binaries copies into the output folder.</summary>
public static class TesseractPaths
{
    public static string DefaultExePath
    {
        get
        {
            var folder = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "_winarm",
                Architecture.X86 => "_win32",
                _ => "_win64",
            };
            return Path.Combine(AppContext.BaseDirectory, folder, "tesseract.exe");
        }
    }

    /// <summary>The eng.traineddata shipped beside the app.</summary>
    public static string BundledTessdataDir => Path.Combine(AppContext.BaseDirectory, "tessdata");

    /// <summary>Writable language directory; bundled eng is copied here so all languages live in one place.</summary>
    public static string DefaultUserTessdataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FGScanner", "tessdata");
}
