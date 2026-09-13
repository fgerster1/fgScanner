# Quick Scan — Phase 0 spike findings

Answers to the three feasibility questions in
[`plans/2026-08-30-quick-scan-program.md`](../plans/2026-08-30-quick-scan-program.md) §6 Phase 0.
Spikes 1 and 3 run 2026-09-12 without scanner hardware; spike 2 needs the scanner and is still open.
No production code was changed and no probe code was kept.

| Spike | Question | Answer | Changes the plan? |
|---|---|---|---|
| 1 | Device-side region scan through NAPS2.Sdk? | **No supported route** | No — confirms Phase 5's software crop |
| 2 | Fastest honest flatbed preview? | **Not yet run** (needs the scanner) | — |
| 3 | Does Simple MAPI work on this machine? | **No** — no MAPI client registered | **Yes** — Phase 10's happy path never fires here |

---

## Spike 1 — Device-side region scan

**Verdict: no supported route.** NAPS2.Sdk 1.3.0 cannot scan an arbitrary X/Y sub-rectangle on any
driver. The only placement control is `ScanOptions.PageAlign` (`HorizontalAlign.Left/Center/Right`),
which is one-axis, three fixed positions, and the vertical offset is always 0.

Inspected: NAPS2.Sdk **1.3.0** from the NuGet cache, reflected, and cross-read against
`cyanfish/naps2` at commit `8ae3e82` (the commit in the DLL's `AssemblyInformationalVersion`).
Reading only; nothing copied.

- **`ScanOptions`** and every nested option type (`TwainOptions`, `WiaOptions`, `EsclOptions`,
  `SaneOptions`) — no `X`, `Y`, `Offset` or `Origin` member. `PageAlign` is the only placement member.
- **TWAIN** — `Scan/Internal/Twain/TwainScanRunner.cs`. NAPS2 *does* set `DAT_IMAGELAYOUT` with a
  `TWFrame`, so the device mechanism exists, but `Top` is hard-coded to 0 and `Left` is computed from
  `PageAlign` alone. A caller cannot drive the frame. (`ICAP_FRAMES` is not used.)
- **WIA** — `Scan/Internal/Wia/WiaScanDriver.cs`. Writes `IPS_XPOS` from `PageAlign`; never writes
  `IPS_YPOS`. `WiaOptions.OffsetWidth` only widens `IPS_XEXTENT` to include that offset.
- **eSCL** — `Scan/Internal/Escl/EsclScanDriver.cs`. The wire type `EsclScanSettings` has real
  `XOffset`/`YOffset`, but NAPS2 sets `XOffset` from `PageAlign` and never sets `YOffset`.
  `EsclOptions` exposes no region passthrough.
- **SANE** — same pattern via `SaneScanAreaController`. Its `KeyValueOptions` escape hatch is
  SANE-only and irrelevant on Windows.

FG Scanner today: `Naps2ScanService.BuildOptions` does not set `PageAlign` at all, and
`PageEdit.Crop(Left, Top, Right, Bottom)` already exists in `src/FgScanner.Scanning/Editing/PageEdit.cs`.

**Consequence:** Phase 5 stands as written — full-area scan at target DPI, then `PageEdit.Crop`.
`PageAlign` cannot approximate a marquee and should not be wired for this purpose. This is the
finding ADR-0007 records.

**Not verified:** behaviour against the Pantum M6550NW's own driver (hardware excluded); method
bodies were read from GitHub source at the matching commit rather than decompiled IL.

---

## Spike 2 — Preview cost

**Not run.** It needs the Pantum, which is in use for evidence capture. An interrupted attempt on
2026-08-30 got as far as a preliminary impression that `ThumbnailSize` was roughly twice as fast as a
low-DPI full-platen scan, but it recorded no measurements — treat that as a lead to test, not a result.

To run: compare a 75–100 dpi full-platen scan against `ScanOptions.ThumbnailSize` on the M6550NW over
TWAIN, several runs each, and report wall-clock from scan start to first usable image.

---

## Spike 3 — Email via Simple MAPI

**Verdict: not viable on this machine.** No MAPI-capable mail client is installed or registered.
The only mail app is **new Outlook**, which does not implement Simple MAPI, so `MAPISendMailW` has
nothing to route to. MAPI works only where classic Outlook (or another MAPI client) is installed and
set as default — the app must treat it as optional at runtime, never assume it.

### Machine evidence (read-only; re-checked independently)

| Check | Found |
|---|---|
| `HKLM\SOFTWARE\Clients\Mail` default value | **empty** |
| `HKLM\SOFTWARE\Clients\Mail` subkeys | only `Hotmail` → `C:\Program Files\Internet Explorer\hmmapi.dll` (a vestigial HTTP-mail stub); same under `WOW6432Node`; no `HKCU` key |
| `HKLM\SOFTWARE\Microsoft\Windows Messaging Subsystem` | key exists, **zero values** (no `MAPI`/`MAPIX`) |
| `mapi32.dll` | OS stub in System32 and SysWOW64, both v1.0.2536.0, never replaced by a client |
| Classic Outlook | absent (no `Office\ClickToRun\Configuration`, no `OUTLOOK.EXE`) |
| New Outlook | `Microsoft.OutlookForWindows_1.2026.818.0_x64` installed |
| `mailto:` handler | new Outlook (packaged app ProgId) |
| Thunderbird / eM Client | absent |

### The Windows 11 picture

- New Outlook has no Simple MAPI; calls route to classic Outlook if present and otherwise fail with
  `MAPI_E_FAILURE`/`MAPI_E_NOT_SUPPORTED`
  ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/4631413/mapisendmail-and-mapisendmailw-return-mapi-e-failu)).
- New Outlook also has no COM automation; Microsoft points to Graph instead
  ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1661764/how-to-open-the-new-outlook-to-send-en-email-with)).
- Where MAPI is used, call `MAPISendMailW` directly; `MAPISendMailHelper` is a down-level shim
  ([MAPISendMailW](https://learn.microsoft.com/en-us/windows/win32/api/mapi/nc-mapi-mapisendmailw)).
- `mailto:` cannot carry attachments by design ([RFC 6068](https://www.rfc-editor.org/rfc/rfc6068.html)).
- NAPS2 copes by offering MAPI *plus* Thunderbird and OAuth web providers (Gmail, Outlook.com), so a
  no-MAPI machine still has a path ([naps2#166](https://github.com/cyanfish/naps2/issues/166),
  [naps2#383](https://github.com/cyanfish/naps2/issues/383)).

### What this means for Phase 10

The plan's Phase 10 treats MAPI as the happy path and Explorer-with-file-selected as the fallback.
On this station **the fallback is the only path that will ever run**, so as written the Email button
never actually produces an email here. Options, in the spike's recommended order:

1. **Windows Share sheet** (`DataTransferManager` via `IDataTransferManagerInterop.GetForWindow`,
   supported for unpackaged WPF —
   [docs](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-send),
   [WPF sample](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/ShareSource/wpf/DataTransferManagerHelper.cs)).
   Carries real file attachments to new Outlook (a packaged share target) and any other share-aware
   app; the operator still presses Send. Cost: needs a Windows-SDK target framework
   (`net10.0-windows10.0.x`) for the WinRT projection — a TFM change, not a new package, but not yet
   verified against this solution. Classic Outlook is not a share target.
2. **Write a `.eml` and shell-open it** — MIME message with attachments, no registry dependency.
   New Outlook's `.eml` handling is partial, so it may open as a read view rather than a draft.
3. **Explorer with the file selected + plain instruction** — the plan's current fallback. Always
   works, never attaches anything.

Classic Outlook COM automation is rejected: it needs classic Outlook, which is absent.

**Runtime detection rule (probe, never invoke):** offer MAPI only when `HKLM\SOFTWARE\Clients\Mail`
has a non-empty default, that client's subkey has a `DLLPathEx`/`DLLPath` pointing at a file that
exists, and `Windows Messaging Subsystem` has a non-empty `MAPI` value. All three are false here.

**Open decision for Phase 10 (Franz's call):** keep MAPI-then-Explorer as planned, or make the Share
sheet the primary route with MAPI as the "classic Outlook present" branch and Explorer as the last
resort. The spike recommends the latter; the plan should be amended before Phase 10 starts.

### Manual tests (need a human — invoking MAPI can open a compose window or send mail)

- [ ] On this machine, `MAPISendMailW` returns an error rather than hanging — record the code.
- [ ] On a machine with classic Outlook as default, MAPI opens a draft with the attachment intact.
- [ ] New Outlook appears in the Share sheet and receives the scanned file as an attachment.
- [ ] Double-clicking a generated `.eml` in new Outlook: draft, read view, or rejected?
