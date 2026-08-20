# Research 3 — OCR + Google AI (COMPLETE, observed 2026-08-19)

## OCR decisions
- **Engine: Tesseract 5.5.3 (Apache-2.0) via SHELL-OUT**, binaries from **NAPS2.Tesseract.Binaries 1.4.0 NuGet** (Apache-2.0, x86/x64/arm64/mac/linux). One recognition pass → multiple renderers: `tesseract img out pdf hocr tsv`. This is what NAPS2/OCRmyPDF do; avoids charlesw P/Invoke DLL hell (231 open issues, x64-only, breaks single-file publish).
- Flags: `--dpi 300 --oem 1 --psm 3` (+psm 1 if no upstream derotation; psm 6 cells; psm 7 line crops; psm 11 sparse). **Ship tessdata_fast** (4.1MB/lang; accuracy ≈ best: 99.18% vs 99.19% char, 1.88× faster). Set TESSDATA_PREFIX on child process only.
- Parallelism: `OMP_THREAD_LIMIT=1` in child env + N processes = physical cores via SemaphoreSlim (3–6× throughput). ~150–400MB RSS/instance.
- Safety: ProcessStartInfo.ArgumentList (never string concat/cmd.exe), absolute exe path, whitelist -l `^[a-z]{3}(\+[a-z]{3})*$`, drain stdout+stderr concurrently, kill on timeout.
- TSV = parse target: cols level/page/block/par/line/word_num/left/top/width/height/conf/text; level 5=word, conf -1 = structure row. hOCR for baseline info.
- Accuracy baseline (UNLV, Tess4 fast, English): CER 1.16–2.04%, word acc 94.45–98.45%. No 5.x official benchmarks exist.
- Preprocessing (Leptonica ships anyway): 300 DPI gray → set DPI explicitly (window sizes scale by DPI!) → background-normalize → deskew → 10px border → Otsu (office) / Sauvola thresholding_method=2 (books/shadows) → despeckle. Cap-height sweet spot 20–40px; don't upsample past 300.
- **LSTM does NOT populate x_font/x_fsize — no bold/italic/size in hOCR.** Heading detection must be geometric (line height percentiles, gaps, width). Tesseract has ZERO table capability.

## OCR→Markdown tiers (recommended architecture)
Front-end all tiers: rasterize 300 DPI (PDFtoImage; NOT PdfiumViewer; OpenCvSharp4 BSD not Emgu GPL) → preprocess → always run Tesseract TSV (offline floor + anchor text).
- **Tier 0 (always, offline):** TSV → C# geometric reconstructor: columns via ink-projection valleys, headings via line-height >1.25× median, lists via marker regex + hanging indent, paragraphs via gap analysis. No tables (emit fenced preformatted). ~1–3 wks.
- **Tier 1 (local structure, optional download):** GLM-OCR 0.9B MIT 2.2GB via Ollama (`ollama pull glm-ocr`, native Markdown+HTML tables, OmniDocBench 95.22 — beats Gemini 3 Pro 92.91) or PP-StructureV3 via Sdcb.PaddleOCR 3.3.1 (Apache; ⚠ full-pipeline exposure unverified — prototype early).
- **Tier 2 (cloud opt-in):** Azure DI Layout `outputContentFormat=markdown` $10/1000pp, Azure.AI.DocumentIntelligence 1.0.0. ~2–4 days effort, best quality/effort.
- **Tier 3 (VLM, hard pages only):** anchor-text prompt (RAW_TEXT_START/END w/ Tesseract text), 1 page/request, temp 0, HTML tables not pipe, strip headers/footers instruction, blank-page short-circuit, post-gen Levenshtein vs anchor <0.75 → reject/fallback.
- Output contract: page_0001.md with YAML front matter (engine, tier, confidence, anchor_similarity, duration_ms).
- License traps: Surya/Marker/Chandra = RAIL-M weights (paid >$5M); GOT-OCR2 NC data; EasyOCR stale 2024; docTR = multi-week ONNX port; Windows.Media.Ocr = word boxes but no confidence, no structure, langs not guaranteed — fallback tier only.

## Searchable PDF
- **Let Tesseract render the PDF** (Tr 3 invisible text, GlyphLessFont, DCT passthrough/CCITT G4 20–50KB/page for bilevel). `-c textonly_pdf=1` → overlay own image PDF via PDFsharp (MIT) when needing PDF/A/encryption/metadata. Merge pages w/ PDFsharp. Word bbox→PDF: x*72/dpi, y flipped, place on baseline, clamp font size to bbox width (NAPS2's ClampFontSizeByRightBound trick).

## Google AI (Part B)
- **Lineup moved past 2.5→3.x; Gemini 2.0 shut down.** ✅ **RECOMMEND `gemini-2.5-flash-lite`: $0.10/M in, $0.40/M out, thinking OFF by default.** Letter page @300DPI = 1,032 tokens (4 tiles × 258; ≤384px flat 258; tiles=ceil(w/cropUnit)×ceil(h/cropUnit), cropUnit=floor(min/1.5)).
- **Cost: $0.21 / 1,000 pages; 5,000 pages ≈ $1.04 ($0.52 batch).** Cost is a non-issue. Alternatives: gemini-3.5-flash-lite $0.95/1000, Claude Haiku 4.5 $3.78/1000, local Ollama $0.
- 🔴 **Thinking tokens billed as output; Gemini 3 models default medium — set thinking_level minimal explicitly.** 🔴 **Free tier trains on user content + human review — paid tier only for real documents; EEA/UK/CH require paid tier contractually.**
- **SDK: official `Google.GenAI` 1.18.0** (googleapis/dotnet-genai, Apache-2.0, weekly releases, no gRPC, Microsoft.Extensions.AI IChatClient built-in) → same abstraction covers local Ollama via OllamaSharp 5.4.30. Avoid Google.Cloud.AIPlatform.V1 (heavy gRPC, Vertex-only).
- **Key model: BYO-key** (user creates own AI Studio key; their quota/bill/data relationship; our liability ~0). Never ship a key or SA JSON. Store in **Windows Credential Manager** (user-visible/revocable), DPAPI CurrentUser fallback; never log; in-app "clear key".
- Vertex AI inappropriate for desktop (no user-owned revocable key); Vertex Standard pricing = Dev API 1.0×.
- Auth: x-goog-api-key header (not ?key=). Inline base64 fine (<20MB); Files API edge case only. generateContent fine ("legacy" but fully supported; Interactions API is the new thing — stateless per-page doesn't need it).
- Config: temperature 0.2, maxOutputTokens 400, safety default = OFF on 2.5/3.x models (doc content OK); check finishReason (SAFETY/RECITATION/MAX_TOKENS) + degrade gracefully.
- ≤1000 chars: prompt aims for ~700, **code-enforced truncation at sentence boundary**; BLANK PAGE sentinel; "don't guess/don't transcribe"; blank-page short-circuit via Tesseract word count (<5 words → skip call, 5–15% savings).
- Retry: exp backoff + jitter on 429/408/5xx, max ~4; on first 429 halve global concurrency. Rate limits no longer published — read from AI Studio, adaptive throttling. Batch API: 50% off, ≤24h, inline <20MB or JSONL ≤2GB — for archive backfill mode.
- Durable per-page queue: NotRequested→Queued→InFlight→Done|Failed(n≤3)|Skipped(offline); never block scan/OCR/export; passive pending indicator; hide feature if no key.
- Prompt (verbatim template in full research output): one paragraph ≤700 chars, doc type → letterhead/names → dates/ref numbers → subject → physical characteristics; no preamble; BLANK PAGE sentinel.

## Verification gaps
Vertex data-governance page unreachable; media_resolution token costs undocumented (measure w/ countTokens); Sdcb PP-StructureV3 exposure; cloud OCR prices second-hand; OpenAI image-token formula unverified.
