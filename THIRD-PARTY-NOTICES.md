# Third-Party Notices

FG Scanner is MIT-licensed (see LICENSE). It ships or references the
components below. This list is kept in sync with `Directory.Packages.props`;
treat drift as a review item.

## Shipped with the application

| Component | License | Use |
|---|---|---|
| NAPS2.Sdk, NAPS2.Sdk.Worker.Win32, NAPS2.Images.Gdi | LGPL-2.1-or-later | Scanning (WIA/TWAIN/eSCL). Shipped as separate, unmodified assemblies; the LGPL text accompanies the app; any SDK modifications would be published under LGPL |
| NAPS2.Tesseract.Binaries (Tesseract 5.5) | Apache-2.0 | OCR engine, run as a child process |
| Leptonica (inside Tesseract) | BSD-2-Clause | Image processing within Tesseract |
| NAPS2.Pdfium.Binaries (PDFium) | BSD-3-Clause | PDF rendering/import |
| PDFsharp (via NAPS2.Sdk) | MIT | PDF assembly and text layer |
| tessdata_fast language models | Apache-2.0 | OCR languages (eng bundled, others downloaded) |
| tessdata_fast osd.traineddata | Apache-2.0 | Page-orientation detection (bundled) |
| Microsoft.EntityFrameworkCore.Sqlite | MIT | Data access |
| SQLitePCLRaw.bundle_e_sqlite3 / SQLite | Apache-2.0 / Public Domain | Database engine |
| ClosedXML | MIT | XLSX index export |
| Google.GenAI | Apache-2.0 | Gemini API client (BYO-key, optional feature) |
| NetSparkleUpdater.SparkleUpdater | MIT | Auto-update (Ed25519-signed appcast) |
| System.CommandLine | MIT | fgscanner CLI |
| ZXing.Net (+ Windows.Compatibility binding) | Apache-2.0 | Patch-T separator detection + separator sheets |
| System.Security.Cryptography.ProtectedData | MIT | DPAPI fallback for key storage |
| Microsoft.Extensions.Hosting | MIT | DI / app lifetime |
| CommunityToolkit.Mvvm | MIT | MVVM |
| Serilog (+ Extensions.Hosting, Sinks.Console, Sinks.File) | Apache-2.0 | Logging |
| .NET Runtime / WPF | MIT | Framework |

## Test-only (never shipped)

| Component | License |
|---|---|
| xunit.v3, xunit.runner.visualstudio | Apache-2.0 |
| Verify.XunitV3 | MIT |
| RichardSzalay.MockHttp | MIT |
| Microsoft.Extensions.TimeProvider.Testing | MIT |
| System.Drawing.Common | MIT |


Excluded by policy (docs/PLAN.md §3 avoid-list): iText (AGPL),
FluentAssertions ≥8, NAPS2.Images.ImageSharp, EPPlus ≥5, Emgu.CV,
System.Data.SQLite.
