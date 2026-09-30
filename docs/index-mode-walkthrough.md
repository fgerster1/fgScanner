# Index mode — Franz's walkthrough (SPEC-2026-008 AC-11)

The one check headless tests cannot do: the real window, the real PKG-0001.
Fifteen minutes. If any numbered step does not do what it says, stop and report
the step number — do not work around it.

## Switch it on (once)

1. Start FG Scanner.
2. Settings → tick **"Index section"** → **Save**.
3. In the same Settings block, check **"Indexer name"** says a real name
   (it starts as your Windows username). Fix it if it says something
   unhelpful, then Save.
4. The left rail now shows **Index** between Groups and Search.

## Open the package

5. Index → **Open package…** → pick
   `C:\Users\Franz Gerster\Visual\JimsStuff\app-data\packages\PKG-0001`.
6. Wait for the check (a few seconds — every file is verified). You should
   see "PKG-0001 - 100 document(s)." and a list of documents on the left,
   in the portal's priority order (mom's-health documents first).
7. **Refusal check:** Open package… again, but pick any ordinary folder
   (e.g. your Documents). A red banner must appear with a plain sentence.
   The section keeps working; open PKG-0001 again.

## Answer three documents

8. **Document 1 — Accepts only.** Pick the first document. Read a page.
   In the right panel, press **Accept** on a subject suggestion and on a
   people suggestion. Both must appear under "Staged answers".
9. **Document 2 — by hand.** Pick another document. Choose a Document
   type; type a date as `2021-07-18` and press **Set**; tick a subject;
   add a person with a role; tick Key document if it is one.
   - Type a date as `07/18/2021` and press Set: it must be refused with
     a sentence, not silently reformatted.
10. **Document 3 — a withdrawal.** Pick a document that arrived with a
    document type already filled (pre-filled combo). Press **Withdraw**
    beside the type. "doc_type —" with an empty value appears in Staged
    answers.
11. **Restart check.** Close FG Scanner completely. Start it, Index,
    Open package… PKG-0001 again. Every staged answer from steps 8–10
    must still be there.

## Export and hand-check

12. Press **Export results**. The line under the toolbar names the file:
    `...\app-data\packages\PKG-0001-results.json`.
13. Press **Email the results file** if you want to see the Gmail compose
    route open with the file on the clipboard (you press Send or close it —
    the app cannot send).
14. Portal check, in a terminal (SCRATCH copy — never the live register):

        cd C:\Users\Franz Gerster\Visual\JimsStuff
        copy app-data\register.db %TEMP%\walkthrough-register.db
        .venv\Scripts\python.exe pipeline\import_index_package.py --db %TEMP%\walkthrough-register.db --results app-data\packages\PKG-0001-results.json --log %TEMP%\walkthrough-log.jsonl

    Expect: written = your answer count, refused = 0.

## Delete gating (do NOT confirm)

15. The **Remove package from this computer** button is enabled (your
    export covers your answers). Tick one more subject anywhere — the
    button must disable. Export again — enabled again.
16. Press it once: a red bar asks with the counts. Press **Cancel**.
    (Keep PKG-0001 on disk — Jim has not indexed it yet.)

Done. Report: which steps passed, any step number that did not, and how the
window felt (slow list? cramped panel?) — that last part is what the spec
cannot know without you.
