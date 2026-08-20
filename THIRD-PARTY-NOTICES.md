# Third-Party Notices

FG Scanner is MIT-licensed (see LICENSE). It uses the following third-party components. This file is updated whenever `Directory.Packages.props` changes; CI treats drift as a review item.

## Referenced today

| Component | License | Use |
|---|---|---|
| Microsoft.Extensions.Hosting | MIT | DI / app lifetime |
| CommunityToolkit.Mvvm | MIT | MVVM |
| Serilog (+ Extensions.Hosting, Sinks.Console, Sinks.File) | Apache-2.0 | Logging |
| xunit.v3, xunit.runner.visualstudio | Apache-2.0 | Testing |
| .NET Runtime / WPF | MIT | Framework |

## Planned (added in later phases, per docs/PLAN.md §3)

| Component | License | Note |
|---|---|---|
| NAPS2.Sdk, NAPS2.Sdk.Worker.Win32, NAPS2.Images.Gdi, NAPS2.Escl | LGPL-2.1-or-later | Scanning. Shipped as separate, unmodified assemblies; LGPL text will be included; modifications to the SDK (if any) will be published under LGPL |
| NAPS2.Tesseract.Binaries (Tesseract 5.5) | Apache-2.0 | OCR engine, run as a child process |
| Leptonica (bundled with Tesseract) | BSD-2-Clause | Image processing inside Tesseract |
| PDFsharp | MIT | PDF assembly |
| PDFium (via PDFtoImage) | BSD-3-Clause | PDF rendering |
| Microsoft.EntityFrameworkCore.Sqlite / SQLitePCLRaw / SQLite | MIT / Apache-2.0 / Public Domain | Data store |
| CsvHelper | MS-PL / Apache-2.0 | CSV export |
| ClosedXML | MIT | XLSX export |
| Google.GenAI | Apache-2.0 | Gemini API client |
| ZXing.Net | Apache-2.0 | Barcode / Patch-T (phase 10) |
| NSubstitute, AwesomeAssertions, Verify, FlaUI, RichardSzalay.MockHttp | BSD/Apache/MIT | Testing |

Excluded by policy (docs/PLAN.md §3 avoid-list): iText (AGPL), FluentAssertions ≥8, NAPS2.Images.ImageSharp, EPPlus ≥5, Emgu.CV, System.Data.SQLite.
