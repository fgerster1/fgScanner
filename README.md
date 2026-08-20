# FG Scanner

An installable, open-source Windows desktop scanning application: NAPS2-class scanning (TWAIN / WIA / eSCL) plus a document indexing layer — per-profile index files (CSV / Excel / XML / JSON) with typed user-defined fields, OCR-to-Markdown sidecars, Google AI image descriptions, and retro-processing of already-scanned folders.

**Status:** planning complete, implementation starting. The approved plan and specification live in [`docs/PLAN.md`](docs/PLAN.md); the research behind every decision is in [`docs/research/`](docs/research/).

## Stack (decided — see docs/PLAN.md §3)

- .NET 10 (LTS) · WPF · NAPS2.Sdk (LGPL-2.1, scanning) · EF Core 10 + SQLite · Tesseract 5.5 (shell-out) · PDFsharp · Google.GenAI (Gemini, user-supplied key) · Inno Setup 7

## License

MIT — see [LICENSE](LICENSE). Third-party components are listed in THIRD-PARTY-NOTICES.md (created in phase 0).
