# Station round trip — Jim's laptop ↔ Franz's PC

How Jim's FG Scanner station travels to Franz, is processed, and comes back
(SPEC-SCANNER-2026-010, ADR-0017). Steps are tagged **[Jim]**, **[Franz]** or **[Claude]**.
Jim's part is two desktop shortcuts and a phone call; everything else is on Franz's side.

## The pieces

| Piece | Where | What it does |
|---|---|---|
| `FGScanner-Jim` | Franz's Google Drive, shared with Jim only | `to-franz\` (Jim's sends) and `to-jim\` (Franz's returns) |
| "Send my scans to Franz" | Jim's desktop | `fgscanner station pack --kind send` into `to-franz` |
| "Install Franz's update" | Jim's desktop | verifies the one return in `to-jim`, shows its code, asks Jim to confirm, then `station apply` |
| `Send-ToFranz-0.5.4.ps1` | placed by Franz, cycle 1 only | the same pack, for 0.5.4 (which has no station verbs) |
| Workspace `Jim` | Franz's PC, `--data-dir` | Jim's station, never mixed with Franz's own |

Every pack is a new folder `<station>-<yyyy-MM-dd-HHmm>[-n]-<send|return>` with
`station-manifest.json` written **last**: no manifest = still uploading (`NOT YET`). A pack
is never edited — a refused one is made again.

## Setup (once, on a Teams call)

1. **[Franz]** On Teams with Jim: Jim's Google account (on his Yahoo address if he has none;
   he types his own password). Google Drive for Desktop installed and signed in on his laptop.
2. **[Claude]** In Franz's Drive (https://drive.google.com): create `FGScanner-Jim` with
   `to-franz` and `to-jim`; stop before Share.
3. **[Franz]** Share `FGScanner-Jim` with Jim's account only ("Restricted", never "anyone with
   the link").
4. **[Jim]** In Drive, "Shared with me" → `FGScanner-Jim` → Add shortcut to My Drive. Then in
   Explorer, right-click `G:\My Drive\FGScanner-Jim\to-jim` → **Available offline**.
5. **[Franz]** Confirm Jim's account can approve UAC (he clicks Yes on a prompt).
6. **[Jim]** Tell Franz, group by group, which box the pages came from (Box values).

## Cycle 1 (Jim on 0.5.4)

1. **[Franz]** Copy `build/station/Send-ToFranz-0.5.4.ps1` to Jim's desktop; on "give control"
   run it with `-To "G:\My Drive\FGScanner-Jim\to-franz"`. It says
   `OK xxxx-xxxx - leave the laptop on until Google Drive says it's done`. Jim reads the code out.
2. **[Claude]** When Drive has finished: `fgscanner station verify <pack>` on Franz's PC → `OK`
   with **the same code**. That pack is Jim's backup: copy it to the Tower backup, then delete
   it from Drive **and from Drive's bin**.
3. **[Claude]** Download the 0.8.1 installer from the release, check it against `SHA256SUMS`,
   put it in `to-jim`. **[Jim]** close FG Scanner → double-click it → **Yes** on UAC → start FG
   Scanner once (this updates his database) → close it → read out the version. During setup tick
   "This is Jim's station" and confirm the Drive folder. **[Franz]**
   `gh release edit v0.8.1 --prerelease=false --latest`. Delete the installer from `to-jim`.
4. **[Claude/Franz]** Open the send into an empty workspace:
   `fgscanner station apply <pack> --data-dir <workspace> --evidence C:\Evidence-Scans`
   (the evidence folder is always typed — a pack never chooses where files land). Process
   there: auto-orient off in the workspace only, Box values, read all pages, Propose, notes,
   commit what is finishable, import committed groups into the portal. Never capture in the
   workspace.
5. **[Claude]** `fgscanner station pack --data-dir <workspace> --kind return --to "<drive>\to-jim"
   --evidence C:\Evidence-Scans --based-on <the send's station-manifest.json> --by "Franz Gerster"`.
   The return carries `changes.jsonl` (every cell changed, with name and time) and `needs-jim.txt`.
6. **[Jim]** "Install Franz's update". It checks the update, shows a code, and asks
   "Is it xxxx-xxxx?". Franz reads the code from his OK line; Jim types **Y** only if they match.
   Then `OK xxxx-xxxx`. **[Franz]** checks Jim's group count on Teams. Delete the return from
   Drive and its bin.

## Later cycles (Jim on 0.8.1 or newer)

The same, with the shortcuts only: Jim's "Send my scans to Franz" ships only what Franz lacks
(a second send with nothing new is the database alone); Franz applies the send into the
workspace (`station apply <pack> --data-dir <workspace>`), processes, returns.
A release is published only when a cycle's return needs it.

## When something says NOT OK

| Message | Meaning | What to do |
|---|---|---|
| `NOT YET — … still arriving` | Drive has not finished | wait, try again |
| `FG Scanner is open` | the app holds its lock | close it, try again |
| `… has changed since it was sent` | Jim scanned or edited while his data was away | nothing was changed; Jim sends again, Franz processes again |
| `… not the one whose code you checked` / `more than one update is waiting` | the folder changed, or an extra return | nothing was changed; Franz clears `to-jim` and sends one return |
| `… names a folder outside this station's evidence folder` / `… has a table this version does not` | the return's database is not what Franz made | nothing was changed; call Franz |
| `could not be fully put back … --undo finishes` | a file was held by another program while putting back | close other programs, run `fgscanner station apply --undo` |

**Undo:** `fgscanner station apply --undo` takes back the last apply, only while the station is
exactly as that apply left it. Replaced and deleted files are kept in `apply-backup-<stamp>\`
(renamed `apply-undone-…` after an undo); nothing is ever erased.

**A refused first update:** Jim's next "Send my scans to Franz" sends everything again (the 0.5.4
sender recorded no fingerprint); Franz starts a fresh workspace from it.
