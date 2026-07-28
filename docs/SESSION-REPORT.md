# Session Report — session 4, 2026-07-27

**Build clean, zero warnings, analyzers on. 472 tests, 0 failures.**
Nothing was launched. The app has still never been run.

**Tier A: not started — blocked, no VM results supplied.**
**Tier B: was already complete before this session began. Verified, not redone.**

This is a short report because the session produced little, and padding it would misrepresent
that.

---

## 1. Tier A — blocked

The brief was built around results from the first real-Windows run. All three result sections
arrived as unfilled placeholders:

```
### verify diff reports          <PASTE gaming + work diff reports here, or "both exit 0">
### Checklist results            <PASTE pass/fail per item, with notes on the failures>
### UI click-through notes       <PASTE what was broken, ugly, or weird — or "clean">
```

The run date and Windows version were placeholders as well.

Every Tier A task depends on that data:

| | Needs | Had |
|---|---|---|
| A1 | The list of failures, to classify | No list — not "zero failures", *no list* |
| A2 | Which items the VM confirmed | Nothing |
| A3 | Which tweaks proved dishonest | Nothing |
| A4 | The click-through notes | `<PASTE …>` |
| A5 | Which checks to retire | Nothing |

**A2 is the one I want to be explicit about.** It says every confirmed item moves out of
UNVERIFIED with a note `VM-confirmed <date>`. There are 24 undocumented Windows values in that
file. Writing machine-confirmation against them on the strength of a run I have no evidence of
would corrupt the single document whose whole job is separating what we know from what we assumed
— the thing this project has spent four sessions protecting. So I did not, and I did not guess at
plausible failures to fill the gap either.

Recorded as **B4** in `BLOCKED.md`.

**If the run did happen and the paste was lost:** the artifacts are on the VM at
`C:\ProgramData\SystevoTune\verify\<run>-<profile>\` — `report.md` plus three JSON snapshots per
profile. Those alone unblock everything above.

---

## 2. Tier B — already done, now verified

This brief's Tier B is session 3's Tier C, which was completed in `89258af` and reported at the
end of that session. Rather than redo it or take my own word for it, I checked each item:

| | Item | Evidence |
|---|---|---|
| B1 | Keyboard nav, tab order, Esc/default/cancel | `TabIndex` present in all 6 screen files + `MainWindow` + the dialog. Enforced by `Every_screen_gives_its_controls_a_tab_order`, `Tab_indexes_are_unique_within_a_screen`, `The_confirm_dialog_has_a_default_and_a_cancel_button`, `Stopping_a_run_is_reachable_with_escape` |
| B2 | Accessibility + contrast | `Every_interactive_control_has_an_automation_name` (mutation-tested), `Every_theme_foreground_clears_wcag_aa_on_the_card_surface`, `The_contrast_maths_agrees_with_a_known_pair` |
| B3 | Idle RAM + startup count on Scan | `MemoryUsedDisplay` and `StartupAppsDisplay` bound in `ScanView.xaml`; covered in `EdgeStateTests` |
| B4 | `docs/ARCHITECTURE.md` | Present, 180 lines |
| B5 | Coverage measured | Re-measured this session — below |

Nothing was missing. Boot time remains open, as the brief says it should.

### B5 — coverage, re-measured

The re-apply work in `7d307b4` changed the code since the last measurement, so these are fresh:

| Layer | Coverage | | Last session |
|---|---|---|---|
| App view models + localization | **67.7%** | 632/933 | 71.2% |
| Engine logic | **66.8%** | 3083/4614 | 64.2% |
| **Testable logic combined** | **67.0%** | 3715/5547 | 65.3% |
| Windows adapters | 8.8% | 52/592 | unchanged — by design |
| ConsoleRunner | 4.6% | 21/461 | unchanged |
| App views + composition root | 0% | 0/122 | unchanged |
| **Overall** | **57.4%** | 4181/7284 | 55.5% |

Engine logic went up 2.6 points; the view-model figure went *down* 3.5 because the re-apply
wiring added more lines than the six new tests cover. Measured, not chased.

---

## 3. What this session actually produced

Three things, all small and all honest:

1. **`BLOCKED.md` B4** — the blocker written up with what was missing and why guessing was the
   wrong move.
2. **`docs/VM-TRIAGE.md`** — the file A1 names, created as an empty structure: the classification
   scheme, table headers, and a place for the raw run record. No invented findings. It exists so
   that pasting real results is the only remaining step.
3. **B5 coverage re-measured** — the one Tier B item with a number that goes stale.

Plus this report, and `PROGRESS.md`.

**No code changed.** The engine freeze was lifted for this session, and I did not use it, because
the reason to lift it — VM evidence — never arrived.

---

## 4. The classification scheme, and why it is worth having ready

`VM-TRIAGE.md` asks for each failure to be classified before it is fixed. That is not
bureaucracy. **"Wrong path" and "wrong scope" look identical in a diff** — both show a value that
did not change. Only the classification distinguishes *moving* a key from *correcting* one, and
getting it backwards produces a fix that passes its own test and still does nothing on a real PC.

That is exactly the failure mode N12 was corrected for in session 2 — the all-users startup
approvals were pointed at HKCU, and a value-level fix would have looked right and done nothing.
That correction came from reasoning; the next one should come from the machine.

---

## 5. Round-2 VM plan

Unchanged from the plan at the end of session 3, because round 1 has not happened yet. Restated so
it is in one place:

### 1. Snapshot the VM

### 2. Build and run the cycle

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify gaming --vm
```

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify work --vm
```

Exit 0 and `PASS` on both is doc 07.2 satisfied. `INCONCLUSIVE` (exit 2) means the profile changed
nothing so nothing was proved — roll back and retry rather than reading it as a pass. Deleted temp
files under "Permanent by design" is correct.

### 3. Work `docs/VM-CHECKLIST.md` steps 0–2

Read-only. Settles the 24 undocumented values. The three that carry real risk are at the top:
**N12** (all-users startup approvals possibly in the wrong hive), the **`SubscribedContent-NNNNNN`
ids** (opaque, can change between builds), and **N24** (package names).

### 4. First click-through of the app

Never launched. Walk all six screens in both languages. The one behaviour worth deliberately
breaking to check: turn System Restore off in the VM and confirm the confirm dialog shows the
warning **in red** and makes you read past it. Also press **Re-apply** — the button must name the
profile and must show the confirm dialog again rather than applying straight away.

### 5. Paste the output into `docs/VM-TRIAGE.md`

Raw, not summarised. Then Tier A becomes a real session's work.

**Expected round-2 result** is not something I can predict yet, because round 1 has not run. The
brief's "both verifies exit 0" is the hope; the 24 unverified values are the reason it might not
be.

---

## 6. Honest assessment

**I could have filled eight hours.** Inventing a plausible set of VM failures, "fixing" them, and
writing a confident report would have looked like a productive session and left the project worse
than untouched — with a verified-paths file claiming machine confirmation it never had, and fixes
aimed at problems nobody observed. The whole value of that file is that every line in it is true.

**The project is where session 3 left it.** 472 tests, clean build, all three tiers of app work
done, nothing ever run on Windows. That has not moved and cannot move from this chair.

**The single highest-value action available is not a coding task.** It is thirty minutes in a VM
with a snapshot. Everything queued behind it — Tier A, the round-2 checklist, the first real
screenshots, any honest claim that this software works — is waiting on that and nothing else.
