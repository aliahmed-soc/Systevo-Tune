# Session Report — autonomous build, 2026-07-26 → 27

All 12 tasks are done. **Build clean, 0 warnings. 245 tests, 0 failures.**

Nothing has run on any machine. That is the single most important line in this report: the
engine is complete and internally consistent, and it is entirely unproven against real Windows.

---

## 1. What was built

| # | Task | Commit |
|---|------|--------|
| 0 | Solution, project `CLAUDE.md`, three skills | `63b44ba` `cd272b2` |
| 1 | ChangeLog — JSONL, one file per run, written before the change | `8dc4f14` |
| 2 | UndoEngine — Undo All / per-run / per-item, partial failure | `8dc4f14` |
| 3 | RestorePointService + the platform abstraction layer | `2e20f6b` |
| 4 | Dry-run framework — `ITweak`, `TweakRunner` | `87554c3` |
| 5 | Cleanup module — scan first, whitelist, user-folder guard | `8c3e0b7` |
| 6 | Power plan switch + undo | `3a02496` |
| 7+8 | Visual effects, Game Mode, Game Bar, GPU scheduling | `939ee89` |
| 9 | Startup manager — list, disable, enable, never delete | `1ff4bbe` |
| 10 | Gaming and Work profiles | `60e5168` |
| 11 | ConsoleRunner — scan, preview, apply, undo | `ba666f6` |
| 12 | Services support (empty whitelist) + metrics | `c013199` |

**No NuGet package was added.** The only ones in the repo are the xUnit test template's.

### The two structural decisions worth knowing

**Preview is enforced by construction, not by discipline.** `ITweak` splits into `PlanAsync`
(reads only) and `ApplyChangeAsync` (applies one already-planned change). The only caller of
`ApplyChangeAsync` is `TweakRunner.ApplyAsync`, which writes the log record first. A tweak has
no code path to the system that skips the log. Adding a tweak that forgets to log is not a
mistake you can make.

**Cleanup deletions are marked `undoable: false`.** They are genuinely permanent. Without that
flag, Undo All would either report them as failures — wrong, nothing failed — or silently claim
to have restored them. Instead `UndoReport.Permanent` lists them so the user is told plainly
what Undo cannot bring back. This is the one field added beyond doc 5.2's example record; it
defaults to `true`, so any record written without it still reads correctly.

---

## 2. Test count and what was actually proven

245 tests. Green tests prove nothing unless they can fail, so the four guards that carry the
safety promise were mutation-checked — each was deliberately broken, the failures observed, and
the break reverted:

| Guard | Tests that fail when it is disabled |
|---|---|
| Undo walks newest-first | 2 |
| Undo continues past a failing step | 1 |
| Cleanup refuses user folders | 9 |
| Services forbidden list | 10 |

The cleanup one plants a `thesis.docx` in Documents and asserts nothing outside the whitelist
is ever passed to delete.

**What tests cannot prove:** every path, GUID, service name, and command below. Those are
assertions about Windows, and only Windows can settle them.

---

## 3. UNVERIFIED — 30 items you must check before anything runs

Full detail with assumed semantics is in `.claude/skills/windows-verified-paths/SKILL.md`.
Every one came from model knowledge, exactly as rule 3 anticipated. None is presented as fact.

### Start with these three — they carry real risk

| # | Item | Why it matters |
|---|---|---|
| **U8** | `{WINDIR}\SoftwareDistribution\Download` | Deleting while the Windows Update service is running may be refused, or may confuse a pending update. Check whether the service should be stopped first. This is the one cleanup path that could plausibly break something. |
| **U22** | `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR::AllowGameDVR` | Writes a **policy** key. Confirm this is the right lever and that it does not leave Group Policy in a state the user cannot undo through the normal Windows UI. A policy the user cannot clear is worse than the setting we were trying to change. |
| **U27** | Startup approvals for the all-users folder | The whitelist assumes these live under **HKCU**. If they are actually under HKLM, disabling an all-users startup item will silently do nothing. |

### The rest

- **U1–U5** restore point: `RPSessionInterval`, `DisableSR`, the `Checkpoint-Computer` command,
  and the English output phrases matched as text. Those phrases will not match on a non-English
  Windows — doc 07.4 lists that as a required case, and the result falls through to `Failed`.
- **U6, U7, U9** cleanup paths: user temp, `{WINDIR}\Temp`, `$Recycle.Bin`.
- **U10–U13** power scheme GUIDs. Doc 5.2's own worked example uses Balanced → High
  Performance, which matches U10 → U11 — corroboration from your plan, not from Microsoft.
- **U14–U16** `powercfg /list`, `powercfg /setactive`, `GetSystemPowerStatus`.
- **U17–U23** registry tweak values. U17–U19 carry a second open question: setting
  `VisualFXSetting` alone may not repaint until Explorer restarts. Check whether
  `SystemParametersInfo` is needed, and whether `UserPreferencesMask` also has to move.
- **U24–U28** startup locations and the approval-value shape. Two more open questions there:
  the exact flag semantics beyond `0x02`/`0x03`, and whether the approval key for a folder item
  includes the `.lnk` extension.
- **U29, U30** `GlobalMemoryStatusEx` layout, and the service `Start` value.

---

## 4. Blocked

**B1 — boot time metric.** Not built. It needs the `System.Diagnostics.EventLog` package to
read event 100 from the Diagnostics-Performance log; `TickCount64` gives uptime, which is a
different thing and would be dishonest to label as boot time. Adding the project's first real
dependency for an optional metric was not a call to make without you. Three options and a
recommendation are in `BLOCKED.md`. The other two metrics — freed space, startup app count —
are done and tested.

**B2 — path verification.** Section 3 above. Not a blocker for the build; a blocker for running
anything.

Nothing else is blocked. `docs/DECISIONS.md` holds 22 decisions made alone, each with its
reason.

---

## 5. Your next steps

### Step 1 — verify the paths

Work through `.claude/skills/windows-verified-paths/SKILL.md`. U8, U22, U27 first. As each is
confirmed, move its row into the verified tables at the top of that file with its Microsoft docs
link and the date. That file is the anti-hallucination gate; it only works if it stays honest.

### Step 2 — decide B1, and fill the services whitelist if you want it

`Whitelists/services.json` ships **empty on purpose** — doc 3.3 wants services *known* safe to
move to Manual, and only you can build that list. The file header documents the entry shape.
The forbidden-list guard refuses Defender, firewall, network, audio, printing, and the sign-in
services whatever you write in it.

### Step 3 — VM snapshot, then the doc 07.2 undo test

Take the snapshot **before** the first command. Everything below runs inside the VM, elevated.

Read-only, safe to run anywhere:

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- scan
```

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- preview gaming
```

Then the doc 07.2 cycle. `apply` and `undo` **refuse to run without `--vm`** — a deliberate
speed bump so a mistyped command on your desktop does nothing at all:

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- apply gaming --vm
```

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- undo --vm
```

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- runs
```

Then compare the VM against its snapshot: power plan, the touched registry keys, startup items.
Any difference is a bug. Doc 07.2 is the authority on what to compare.

**Expect this in the undo output, and it is correct:** the deleted temp files are reported as
permanent and not restored. Everything else should go back exactly.

**One thing the harness does that the WPF app must not.** When a restore point cannot be
created, `apply` prints that doc 5.1 requires asking the user, and continues anyway so the VM
test can run unattended. There is no user in a headless harness. The Phase 3 app has to stop and
ask. It is called out in the code, in `DECISIONS.md` as decision 22, and here.

---

## 6. Honest assessment

What I am confident in: the safety layer. Log-before-change is structural rather than a
convention, undo ordering and partial failure are mutation-tested, and the two destructive
guards — user folders and forbidden services — both bite when removed.

What I am not confident in: every single Windows path. That is not modesty, it is the thing
doc 09 warned about in writing — "any AI model can invent a registry key that looks real but is
not". I have flagged all 30 rather than quietly shipping them, which is the most the rules allow
me to do without you. Until they are checked, treat the engine as a well-tested piece of
software that may be aimed at the wrong targets.
