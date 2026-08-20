# Research 2 — Scanning stack & language choice (COMPLETE)

## TL;DR
**.NET 10 (LTS) + WPF + NAPS2.Sdk 1.3.0 (LGPL-2.1) is the unambiguous winner.** Python loses decisively on TWAIN: pytwain is GPLv2, no 32-bit worker model, its own docs say run 32-bit Python; no eSCL library; PyInstaller AV false positives.

## Driver stack priority (matches NAPS2's own portfolio)
1. WIA 2.0 — default for USB/local scanners (no bitness problem)
2. TWAIN 2.x via twaindsm.dll + **32-bit worker process** (mandatory: many vendor DSes still 32-bit; HP TWAIN 32-bit only; no ARM64 TWAIN — gate off on ARM64)
3. eSCL (Mopria) — network MFPs, driverless; Windows 10 KB5014666+/Win11 support it natively
4. Skip: WSD (declining, Win11 24H2 removes under Protected Print), TWAIN Direct (dead in market)

- TWAIN spec current = 2.5 (Nov 2021). WIA docs archived (legacy but functional). WIA Automation layer (wiaaut.dll) CANNOT do duplex (MS KB2709992) — need low-level COM, or just use NAPS2.Sdk which wraps it.

## NAPS2.Sdk key facts
- NuGet 1.3.0, published 2026-07-20, targets .NET 8 / .NET 10 / .NET Fx 4.6.2. ~104K downloads.
- **License: LGPL-2.1-or-later** (SDK, NAPS2.Escl, NAPS2.Images.*); desktop app is GPL-2.0+; samples MIT.
- Wraps WIA + TWAIN (vendored NTwain fork) + eSCL (NAPS2.Escl — only maintained .NET eSCL lib) + SANE + Apple ICA.
- Worker: `ScanningContext.SetUpWin32Worker()` + `NAPS2.Sdk.Worker.Win32` pkg ships prebuilt x86 NAPS2.Worker.exe, gRPC over named pipes.
- API: `ScanController.Scan(options)` → `IAsyncEnumerable<ProcessedImage>`; PaperSource = Flatbed/Feeder/Duplex.
- Repo very active: 4.4k stars, last commit 2026-08-10; app v8.3.2 (2026-07-22), .NET 10 since 8.3.0.
- Image handler pkgs: NAPS2.Images.Gdi / .Wpf / .ImageSharp — UI-framework agnostic scanning layer.

## Alternatives (rejected)
- NTwain 3.7.6 MIT active but no worker/WIA/eSCL; TwainDotNet archived 2020; Saraff stale; Dynamsoft .NET TWAIN retired 2025-12-31; Dynamic Web TWAIN ~$1.4k+ browser-oriented; Atalasoft/LEADTOOLS quote-only; TwainScanning.NET $199 one-time (commercial escape hatch).

## .NET 10 / UI
- .NET 10: GA Nov 11 2025, **LTS until Nov 14 2028**. .NET 8 & 9 both EOL Nov 10 2026. Target net10.0.
- **WPF recommended**: virtualized editable DataGrid built-in (metadata grid), Fluent theme w/ Light/Dark/System, mature imaging. No AOT/trimming (WPF excluded — fine). Thumbnail wrap-grid needs community VirtualizingWrapPanel pkg (currency unverified).
- WinForms runner-up (dark mode via Application.SetColorMode in .NET 10, but no virtualized thumbnail panel).
- WinUI 3 disqualified: no first-party DataGrid; CommunityToolkit DataGrid archived Feb 25 2026.
- Avalonia (MIT, 31.4k★) only if cross-platform becomes committed req; DataGrid virtualization weakness.
- MAUI / Eto: no.

## Decision matrix (1–5): .NET vs Python
TWAIN 5/1 · eSCL 5/1 · GUI 5/4 · Packaging 5/2 · OCR 5/4 · batch perf 5/3 · AI 4/5 · hiring 5/3 · OSS licensing 5/4 · maintenance 5/2.
Python costs: build own TWAIN worker+IPC, GPLv2 pytwain or $670/dev PyQt6, no eSCL, WIA duplex dead-end, PyInstaller AV hell (issues #7976/#8164), 100MB+ bundles, 1–3s cold start.

## Imaging in .NET
- System.Drawing.Common: MIT, Windows-only since .NET 6, maintenance mode — don't build pipeline on it.
- **ImageSharp 4.1.0**: Six Labors Split License — free if app is OSI-licensed OSS or <$1M revenue; else $799–$4,999/yr. NAPS2.Sdk cross-platform handler uses it.
- SkiaSharp MIT but NO TIFF (disqualifying alone). Magick.NET Apache-2.0 but 94.5MB. WIC: OS-native, fast, multi-frame TIFF, needs COM plumbing. Avoid Emgu CV (GPL); OpenCvSharp (Apache) if CV needed.

## PDF
- **PDFsharp 6.2.4 (MIT)** recommended: invisible-text OCR layer possible; PDF/A conformance levels underdocumented (⚠).
- NAPS2's own pipeline (inherited if exporting via SDK): cyanfish/naps2-pdfsharp fork (ImageSharp-backed) + cyanfish/naps2-tesseract binaries; writes invisible text layer.
- iText7+pdfOCR = AGPL or $$$ — avoid. PdfPig read-only. QuestPDF unproven for scan-PDF. Preview rendering: PDFtoImage 5.3.0 (active).
- OCR lib: **TesseractOCR NuGet 5.5.2 (Apache 2.0, Mar 2026)** — word-level bounding boxes RIL_WORD. Windows.Media.Ocr free alt but needs package identity (⚠).

## Licensing summary
OSS app: everything free incl. ImageSharp + NAPS2.Sdk (LGPL). Closed commercial: still free except ImageSharp >$1M revenue → $799+/yr; avoid iText/Emgu.

## Open uncertainties (don't affect recommendation)
PDFsharp PDF/A levels; QuestPDF PDF/A; commercial pricing (quote-gated); Kodak/Xerox 64-bit TWAIN; VirtualizingWrapPanel currency; Windows.Media.Ocr unpackaged workarounds.
