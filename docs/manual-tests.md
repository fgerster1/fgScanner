# Manual hardware smoke tests

Run before each release, and after any change to FgScanner.Scanning. Automated tests cover all logic with FakeScanService; this checklist covers what only real hardware can prove.

## Setup
- [ ] Launch `FgScanner.exe` (real drivers) — app starts, Scan section visible
- [ ] Launch `FgScanner.exe --fake-scanner` — 3 fake devices listed, scan produces pages

## Device discovery
- [ ] WIA: physical USB scanner appears in device list after Refresh
- [ ] TWAIN: same scanner appears under TWAIN driver (32-bit worker starts; check Task Manager for NAPS2.Worker.exe)
- [ ] eSCL: network MFP appears (same subnet, mDNS allowed through firewall)

## Scanning
- [ ] WIA flatbed scan at 300 DPI Color → one page thumbnail, file in %APPDATA%\FGScanner\recovery\<session>\
- [ ] Feeder scan with 3+ pages → thumbnails stream in one at a time
- [ ] Duplex scan (if hardware supports) → front/back pages in order
- [ ] BlackWhite bit depth + 150 DPI → smaller file, still legible
- [ ] Cancel mid-feeder-run → already-scanned pages remain, status shows canceled
- [ ] Empty feeder → error surfaces in status text, app stays responsive

## Crash recovery
- [ ] Start a feeder scan, kill FgScanner.exe from Task Manager mid-scan
- [ ] Relaunch → recovery prompt shows correct page count → Yes → pages appear in list
- [ ] Repeat, answer No → pages discarded, no prompt on next launch
- [ ] Clean exit → no recovery prompt on next launch

## TWAIN specifics
- [ ] TWAIN scan works with a 32-bit-only vendor driver (e.g. older Canon/HP)
- [ ] Unplugging device mid-scan → error in status, no app crash
