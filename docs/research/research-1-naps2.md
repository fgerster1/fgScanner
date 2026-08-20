# Research 1 — NAPS2 dossier (COMPLETE) — v8.3.2, SDK 1.3.0, researched 2026-08-19

## Licensing (verified)
- NAPS2 app + NAPS2.Lib = **GPL-2.0-or-later**. NAPS2.Sdk / Images.* / Escl.* / Worker = **LGPL-2.1-or-later** (dual w/ GPL; author says pick LGPL alone — discussion #880). Samples MIT.
- Fork app → GPL forever, no CLA so no relicense possible; rebrand required (trademark unregistered but passing-off). Consume SDK → any license OK (MIT/Apache), even closed commercial (author explicitly OK, issue #120): keep DLLs separate (no ILMerge/single-file bundling of them), ship LGPL text, state usage.
- Traps: NAPS2.Sane.Binaries GPL-2.0-only (Mac only — avoid); NAPS2.Images.ImageSharp → Six Labors Split License (avoid: use NAPS2.Images.Gdi or .Wpf). Tesseract binaries Apache-2.0 (separate process). Pdfium actually BSD-3-Clause. No NOTICE file upstream — assemble own attributions. FatCow icons CC BY 3.0 (don't reuse).
- Min ref set: NAPS2.Sdk + one NAPS2.Images.* + NAPS2.Sdk.Worker.Win32.
- No Ghostscript anywhere.

## Feature inventory highlights (for parity checklist)
### Drivers
WIA (1.0/2.0 selectable), TWAIN (32-bit worker; DSM new/newX64/old; transfer memory/native), ESCL incl. manual IP + caching, SANE (Linux/Mac), Apple ICA. Device picker w/ icon/list view, "Always Ask", capability-aware profile UI.

### Profile settings (ScanProfile.cs)
DisplayName, Device, Driver, UseNativeUI, PaperSource Glass/Feeder/Duplex, BitDepth 24bit/Gray/BW, PageSize (Letter/Legal/A5/A4/A3/B5/B4/Custom + saved presets, in/cm/mm), PageAlign, Resolution (100–4800 + arbitrary custom), AfterScanScale 1:1..1:8, Brightness/Contrast ±1000, IsDefault/IsLocked/IsDeviceLocked.
Advanced: MaxQuality, Quality 0–100(75), ExcludeBlankPages + WhiteThreshold(70) + CoverageThreshold(15), AutoDeskew, BrightnessContrastAfterScan, WiaOffsetWidth, ForcePageSize/Crop, FlipDuplexedPages, WiaVersion, TwainImpl, TwainProgress, RotateDegrees, WiaDelayBetweenScans, KeyValueOptions.
AutoSave: FilePath+placeholders, PromptForFilePath, ClearImagesAfterSaving, Separator None/FilePerPage/FilePerScan/PatchT.
Sidebar (8.0), 68 rebindable keyboard shortcuts (F2–F12 = profiles 1–11, Ctrl+Enter scan, etc.).

### Editing
View, Crop, Brightness/Contrast, Hue/Sat, B&W threshold, Sharpen, Document Correction (None/Document/Photo), Split, Combine, Edit With external app, Reset; Rotate L/R, Flip, Deskew, Custom rotation; Move Up/Down, Interleave/Deinterleave/Alt/Reverse; Manual Duplex; Delete/Clear/SelectAll/Copy/Paste/Undo-Redo (deletions not undoable); "apply to all selected"; thumbnails w/ size + page numbers, drag-drop reorder; preview window w/ zoom + arrow paging.
NOT present: erase/touch-up, draggable crop box, perspective correction, orientation auto-detect.

### Output
PDF: Default/PdfA1B/A2B/A3B/A3U; metadata Author/Creator/Keywords/Subject/Title; encryption user+owner pw + 8 permission flags. NO PDF compression control (top complaint cluster #44/#80/#324/#614).
Images: JPEG/PNG/TIFF/BMP; JpegQuality(75); TiffCompression Auto/LZW/CCITT4/None; multi-page TIFF default.
Split: None/PerPage/PerScan/PatchT; CLI --splitsize N.
Placeholders: $(YYYY) $(YY) $(MM) $(DD) $(hh) $(mm) $(ss), $(n)/$(nn)/$(nnn)/$(nnnn). No barcode/OCR/prompt/routing placeholders.
Email: MAPI, SMTP, Gmail OAuth, New Outlook, OWA, Thunderbird, Apple Mail. Print. Clipboard custom format. Drag-drop in/out.

### OCR
Tesseract 5.5.0 child process only. 107 langs on-demand download, fast/best variants. Modes Fast/Best (+preprocess "fix white balance/remove noise"). Multi-language. OcrAfterScanning eager option. Invisible PDF text layer at save; import keeps existing text. Timeout 600s. NO text-file export (documented gap — our .md feature fills this).

### CLI (NAPS2.Console) — 60 options
-o/--output, -e/--email, -a/--autosave; --listdevices, --install; -p/--profile, --noprofile, --device, --driver; --source glass|feeder|duplex, --pagesize, --dpi, --bitdepth; --deskew, --rotate; -i/--import w/ slice notation, --importpassword; --pdftitle/author/subject/keywords, --usesavedmetadata, --encryptconfig, --pdfcompat; --jpegquality, --tiffcomp; --to/cc/bcc/subject/body/autosend/silentsend; --enableocr/--disableocr/--ocrlang; --interleave etc; --split/--splitscans/--splitpatcht/--splitsize; -v, --progress, -n, -d, --waitscan, --firstnow, -f, -w.

### Batch dialog
Single/multiple-prompt/multiple-delay; count; interval; output Load/SingleFile/MultipleFiles; separator per scan/page/PatchT (≥300 DPI). No script hook (#106 = top-reacted request).

### Config
profiles.xml + config.xml at %APPDATA%\NAPS2 (portable: <exe>\..\Data); admin appsettings.xml w/ mode=default|lock per element; ~40 admin settings incl. hide-buttons, OcrState, EsclSecurityPolicy, EventLogging. 46 UI languages. Event log via /CreateEventSource.

### Integration
Import PDF (incl. encrypted)/images/ZIP; file associations; StillImage/AutoPlay "Scan with NAPS2"; Scanner sharing = ESCL server (mDNS, ports 9801–9850/9901–9950, HTTPS self-signed, "Share as Service" since 8.2.0); session restore; crash recovery (recovery folder + .lock + index.xml throttled 100ms, refcounted ProcessedImage, file-based storage); update check.

### Platform
Win 10 1607+/11 x64+ARM64 (ARM64: no TWAIN), macOS 12+, Linux GTK3. Installers: Inno .exe, MSI (WiX), portable ZIP, MS Store; deb/rpm/flatpak; EV code-signed. .NET 10, SelfContained, no trimming.

## Architecture
31 projects. Eto.Forms 2.11 UI (WinForms backend on Windows — NOT WPF). Worker: NAPS2.Worker.exe x86, gRPC over named pipes (GrpcDotNetNamedPipes), Job object ties lifetime to parent, WorkerPool warm spares; isolates TWAIN/WIA1.0-UI/MAPI/PDFium. Images stay on filesystem, not crossing IPC. ScanPerformer→ScanController→ScanBridge→RemoteScanController→IScanDriver. Extension seams: IOcrEngine (cleanest public seam), IPdfRenderer, ITiffWriter, ImageContext. No plugin system.

## Gaps/complaints to exploit (our differentiators)
- No DMS layer: no library/tags/custom fields/search — exactly our feature space.
- #113: metadata prompt at save w/ dropdowns + folder routing — our CSV/index feature.
- No OCR text export (#765/#553) — our .md files.
- No post-save script/webhook (#106 top request). No hot folders (#660). No barcode split (#314) or barcode filename (#152). No page-count split GUI (#111).
- No cloud destinations. PDF 96-PPI embed bug (#843). OCR overlay defects (#551). Edit-during-scan freezes. No pre-scan preview. Profile-switching friction (ADF↔glass) top review complaint. Concurrent autosave collision data-loss (#823).

## Caveats
No Reddit sentiment (blocked). README/docs stale in spots. SANE macOS licensing unresolved (irrelevant to Windows-only).
