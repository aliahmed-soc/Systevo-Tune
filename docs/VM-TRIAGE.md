# VM Triage

> **Status: empty and waiting.** No VM run has been reported yet. Nothing below is filled in,
> because nothing has been observed. See `BLOCKED.md` B4.

This is where a real-Windows run gets turned into work. One line per failure, classified, *before*
any code is touched — the classification is what stops a wrong-scope bug being "fixed" by changing
a value, or a detection bug being "fixed" by changing a path.

## How to use it

1. Run the VM cycle (`docs/VM-CHECKLIST.md`).
2. Paste the raw output into **Run record** below. Raw, not summarised — the summary is what the
   triage produces, and starting from a summary loses the detail that decides the classification.
3. Fill the **Findings** table. One row per failure. Classify before fixing.
4. Only then start fixing. A fix is done when code, tests and the `windows-verified-paths` entry
   all agree.

## Classification

| Class | Means | Typical fix |
|---|---|---|
| **wrong path** | The key or folder does not exist as written | Correct the whitelist entry; re-check the docs |
| **wrong value** | Path is right, the data we write is not | Correct the value; check what Windows itself writes |
| **wrong scope** | Right name, wrong hive or context (HKCU vs HKLM, per-user vs machine) | Move it; check whether undo needs to move too |
| **works but detection wrong** | The change lands, but preview/status reports it incorrectly | Fix the read path, not the write path |
| **undo incomplete** | Applied fine, did not fully come back | The most serious class — the product promise is undo |

**Why the classes matter.** "Wrong scope" and "wrong path" look identical in a diff — both show a
value that did not change. Only the classification distinguishes moving a key from correcting one,
and getting that backwards produces a fix that passes its own test and still does nothing on a real
PC. That is precisely the failure mode N12 was corrected for in session 2, from reasoning alone.

## Run record

_Awaiting the first VM run._

```
Date:
Windows version / edition:
verify gaming --vm  → exit code:
verify work --vm    → exit code:
```

Paste `report.md` from `C:\ProgramData\SystevoTune\verify\<run>-<profile>\` for each profile.

## Findings

_Awaiting the first VM run._

| # | Item | Symptom observed | Class | Fix | Status |
|---|---|---|---|---|---|

## Items the run confirmed

_Awaiting the first VM run._

Each confirmed item moves from Tier 2 to Tier 1 in
`.claude/skills/windows-verified-paths/SKILL.md`, annotated `VM-confirmed <date>`. **That
annotation is only ever written from an observed run**, never from reasoning, however confident.

## Items still failing after the fix attempt

_Awaiting the first VM run._

Anything still broken after a genuine attempt goes to `BLOCKED.md` with the analysis and what to
check next time — not left half-fixed in the code.
