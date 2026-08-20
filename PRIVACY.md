# FG Scanner Privacy Policy

*Effective 2026-08-20 · applies to FG Scanner for Windows*

## The short version

FG Scanner processes your documents **on your computer**. Scanning, OCR,
indexing, editing, and export never transmit anything. There is no telemetry,
no analytics, no account, and no advertising.

Exactly two features touch the network, both under your control:

## 1. AI page descriptions (optional, off by default)

If you enable AI descriptions and paste your own Google AI Studio API key,
each page image you choose to describe is sent to **Google's Gemini API**
under **your** Google account and **your** agreement with Google's
[Gemini API terms](https://ai.google.dev/gemini-api/terms).

- The feature is hidden until a key is stored; the first enable shows a
  consent notice, and every run shows a cost estimate first.
- **Google's free tier may use submitted content for model training and
  allows human review.** Use a paid-tier key for real documents. Users in
  the EEA, UK, and Switzerland are contractually required by Google to use
  the paid tier.
- Your key is stored in Windows Credential Manager (visible and revocable in
  the Windows UI), never logged, and removable with "Clear stored key".
- The installer offers a machine-wide opt-out that disables the feature
  entirely.
- Pages whose OCR found no meaningful content are marked "BLANK PAGE"
  locally and are never sent.

## 2. Downloads the app performs at your request

- **OCR language packs** from the Tesseract project's GitHub repository,
  verified against pinned SHA-256 checksums.
- **Update check** (can be disabled in Settings): downloads a version
  manifest from this project's GitHub Releases. No identifiers beyond a
  normal HTTP request are sent.

## Data on your computer

Groups live where you put them. App data (database, trash, OCR languages,
logs) lives under `%APPDATA%\FGScanner` and `%LOCALAPPDATA%\FGScanner`.
Deleting those folders and your group folders removes everything.
Logs never contain document contents or API keys.

## Contact

Questions: open an issue at <https://github.com/fgerster1/fgScanner/issues>.
