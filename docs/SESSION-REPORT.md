# Session Report — autonomous build session 2, 2026-07-27

**Build clean, 0 warnings. 365 tests, 0 failures.** Nothing ran against the dev machine.

Tasks 1–8 done. Task 9 (WPF scaffold) deliberately not started — see the end.

---

## 1. Done

| # | Task | Commit |
|---|------|--------|
| 1 | O1 closed — power schemes resolved at runtime, never assumed | `c2aa4db` |
| 2 | O2–O5 closed | `c280620` |
| 3 | B3 implemented as you decided | `4994a55` |
| 4 | Session-1 leftovers — only B1 remains, and it needs you | — |
| 5 | Privacy module | `6542392` |
| 6 | Bloatware remover engine | `b222333` |
| 7 | Re-apply last profile | `708ac0f` |
| 8 | **VM verification harness + checklist** | `0a5c9a5` |

Still no NuGet package beyond the xUnit test template.

### The five guards that are mutation-checked

Each was deliberately broken, the failures observed, and the break reverted:

| Guard | Tests that fail without it |
|---|---|
| Undo walks newest-first | 2 |
| Undo continues past a failing step | 1 |
| Cleanup refuses user folders | 9 |
| Services forbidden list | 10 |
| Update-cache service exception stays scoped | 5 |

### Three real bugs found while building

1. **`FakePowerPlanService` was too permissive.** Tightening it to refuse activating a scheme it
   does not hold immediately exposed a live bug: after a failed scheme creation, the tweak went on
   to activate a scheme that was never made.
2. **`apply` built the profile twice.** The cleanup summary read fresh tweak instances whose
   `LastApply` was still null, so cleanup detail had never actually printed.
3. **`BloatwareUndoHandler` was written but never registered.** Undoing an app removal would have
   reported "no undo handler registered".

---

## 2. Your decisions, as implemented

**H1/H2 — the update cache.** Stop `wuauserv` and `bits`, delete, restart both. A service that
will not stop skips the group with a warning; nothing is force-killed. A refusal puts back whatever
was already stopped. The restart runs in a `finally` with `CancellationToken.None`, so a cancelled
or throwing delete still brings the services back. A service that will not *restart* is a loud
failure, not a warning — leaving Windows Update down is worse than not cleaning.

The "only exception" is **enforced, not trusted**: `CleanupWhitelist` refuses at load time any
group but `windows-update-cache` naming `stopServices`, and any service but those two. Adding
`"stopServices": ["WinDefend"]` to the JSON gets a refusal, not a disabled Defender.

The group stays out of both profiles (decision 23). Your brief resolved *how* to clean it, not
whether a preset should do it unasked — say the word and I will add it.

**H3 — no UI.** Honoured.

---

## 3. All six open questions are closed

| # | Was | Now |
|---|---|---|
| O1 | Scheme GUIDs assumed | Resolved at runtime: exact GUID → a scheme we created → by name. High Performance can be copied from its template; undo deletes the copy. Ultimate is never invented. |
| O2 | Active scheme read from a `*` in undocumented output | `powercfg /getactivescheme`, a documented option |
| O3 | Restore-point verdict from English prose | Counted with `Get-ComputerRestorePoint`. An Arabic-prose test asserts the verdict still lands. |
| O4 | "Not available on this PC" claimed knowledge we lacked | Absent means Windows is deciding; the message says so |
| O5 | Two undocumented registry reads were the authority | Demoted to a hint; the counts decide |
| O6 | `AppCaptureEnabled` missing | Added, along with `HistoricalCaptureEnabled` |

---

## 4. What is still UNVERIFIED

Full detail in `.claude/skills/windows-verified-paths/SKILL.md`. **`docs/VM-CHECKLIST.md` maps
every one to the exact command that proves it.**

**Verified against Microsoft docs (14):** power scheme GUIDs V1–V3, `powercfg /list` and
`/setactive`, `Checkpoint-Computer` and its once-a-day message, `SYSTEM_POWER_STATUS`,
`MEMORYSTATUSEX`, service `Start` values, `AllowGameDVR`, `AllowTelemetry`.

**Undocumented by Microsoft (24):** N1–N24. Visual effects, Game Mode, Game Bar, GPU scheduling,
startup approvals, System Restore detection, ContentDeliveryManager, the Ultimate Performance
GUID, cleanup paths, and the bloatware package names. Microsoft publishes no reference for any of
it — that is the finding, not a gap in searching.

### The three that carry real risk

1. **N12 — all-users startup approvals.** The whitelist pairs the `ProgramData` Startup folder
   with **HKLM**. If it is actually HKCU, disabling an all-users startup item silently does
   nothing. Checklist step 1 has the exact experiment.
2. **N18–N23 — the `SubscribedContent-NNNNNN` ids.** Opaque Microsoft content numbers, the least
   trustworthy entries in the whole project. They can change between Windows builds; a stale id
   quietly does nothing. Toggle each Settings switch and see which id moves.
3. **N24 — bloatware package names.** Currently harmless because nothing is approved, but confirm
   each with `Get-AppxPackage` before flipping any `approved` flag.

### Two edition caveats worth carrying

`AllowGameDVR` and `AllowTelemetry` both list Pro/Enterprise/Education/IoT and **not Home**. On a
Home VM expect them to do nothing. Home is a very common gaming PC.

---

## 5. Blocked

**B1 — boot time metric.** Unchanged; your brief did not cover it. Needs the
`System.Diagnostics.EventLog` package to read event 100. My recommendation is still to drop it or
show uptime instead — boot time varies too much between boots to make a decent before/after claim.
Freed space and startup app count are the honest numbers.

**B2/B3 — resolved.** B3 by you; B2 by the documentation pass.

Nothing else is blocked. `docs/DECISIONS.md` holds 39 decisions plus your three.

---

## 6. Your next actions

### 1. Review `docs/DECISIONS.md`

Decisions 31–39 are this session's. The four worth your eye:

- **31** — Gaming does Ultimate-if-present → High-if-present → *create High*. Ultimate is never
  invented: it parks fewer cores and is a bigger change than a tune-up should make on a machine
  that never offered it. Disagree and it is a one-line whitelist change.
- **38** — the telemetry tweak writes `1` (required only), not `0`, and is named to match, because
  Microsoft says `0` is Enterprise-only and behaves as `1` everywhere else.
- **39** — privacy leaves the Spotlight *wallpaper* alone and removes only the overlay advert.
- **23** — the update cache is still out of both profiles.

### 2. Work through `docs/VM-CHECKLIST.md` steps 0–2

All read-only. Step 0 records the VM's edition, which several assumptions depend on.

### 3. VM snapshot, then run `verify`

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify gaming --vm
```

Snapshot → apply → snapshot → Undo All → snapshot → diff, in one command. Exit code **0 and
`PASS`** is doc 07.2 satisfied. Artifacts land in
`C:\ProgramData\SystevoTune\verify\<run>-<profile>\`: three JSON snapshots and a Markdown report.

Two things to expect and not misread:

- **`INCONCLUSIVE`** (exit 2) means the profile changed nothing, so nothing was proved. Roll back
  to a clean snapshot rather than treating it as a pass.
- Deleted temp files appear under **"Permanent by design"**. Correct, not a failure.

Then repeat for `work`, and do the manual half the harness cannot: reboot and confirm the settings
survive, and that Windows Update still works after the cache cleanup.

### 4. Then, if you want them

- Fill `Whitelists/services.json` — ships empty by design.
- Flip `approved: true` on bloatware entries you have confirmed and want gone.
- Decide B1.

---

## 7. Honest assessment

**Stronger than last session in one specific way:** the engine no longer assumes. O1 was the bug
class that would have bitten hardest — a tweak reporting success while silently doing nothing —
and closing it forced the same defensive shape everywhere else: match at runtime, read numbers not
words, count things rather than parse prose, and say plainly when we cannot tell.

**The `verify` command is the real deliverable.** Doc 07.2 was a manual procedure nobody would run
consistently. It is now one command with an exit code, and it refuses to report a false pass when
nothing was applied.

**What I am still not confident in:** the 24 undocumented values. That is unchanged, and no amount
of further research will fix it — Microsoft does not publish them. The VM is the only thing that
can settle it, which is why the checklist is written the way it is. Until then, treat this as
well-tested software that may be aimed at some wrong targets.

**Task 9, the WPF scaffold, was not started.** Tasks 1–8 took the session, and 8 was the stated
priority. Starting a UI project I could not finish and test would have left the repo in a worse
state than not starting it, so I stopped at a clean, green, fully committed point instead.
