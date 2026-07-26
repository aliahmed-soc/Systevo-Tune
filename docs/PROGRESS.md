# Progress

Working memory for the autonomous session. If context resets, read this first,
then `DECISIONS.md` and `BLOCKED.md`, then continue from **Next**.

Session ran 2026-07-26 into 2026-07-27. Human unavailable — decisions logged, not asked.

## Done — all 12 tasks

| # | Task | Commit | Tests after |
|---|------|--------|-------------|
| 0 | Solution, CLAUDE.md, three skills | `63b44ba` `cd272b2` | 1 |
| 1 | ChangeLog — JSONL, one file per run, log-before-change | `8dc4f14` | 37 |
| 2 | UndoEngine — Undo All / per-run / per-item, partial failure | `8dc4f14` | 37 |
| 3 | RestorePointService + platform abstraction layer | `2e20f6b` | 53 |
| 4 | Dry-run framework — `ITweak`, `TweakRunner` | `87554c3` | 72 |
| 5 | Cleanup module — scan first, whitelist, user-folder guard | `8c3e0b7` | 115 |
| 6 | Power plan switch + undo | `3a02496` | 140 |
| 7+8 | Visual effects, Game Mode, Game Bar, GPU scheduling | `939ee89` | 158 |
| 9 | Startup manager — list, disable, enable, never delete | `1ff4bbe` | 176 |
| 10 | Gaming and Work profiles | `60e5168` | 194 |
| 11 | ConsoleRunner — scan, preview, apply, undo | `ba666f6` | 215 |
| 12 | Services support (empty whitelist) + metrics | `c013199` | 245 |

Build clean, 0 warnings. 245 tests, 0 failures.

## In progress

Nothing. The session's task list is complete.

## Next — for the human, not for a resumed session

The engine is finished but **unproven**. Nothing has run on any machine. In order:

1. **Verify the paths.** 30 items sit under UNVERIFIED in
   `.claude/skills/windows-verified-paths/SKILL.md` (U1–U30). Start with U8, U22, U27 — those
   three carry real risk. See `SESSION-REPORT.md`.
2. **Decide B1** in `BLOCKED.md` — boot time metric, three options with a recommendation.
3. **Fill `Whitelists/services.json`** if services tuning is wanted. It ships empty by design.
4. **VM snapshot, then the doc 07.2 undo test.** Exact commands are in `SESSION-REPORT.md`.

## Standing rules honoured this session

- Nothing ran against this machine. `dotnet build`, `dotnet test`, `git`, file edits only.
  `C:\ProgramData\SystevoTune` does not exist here; the ConsoleRunner was never executed.
- Every system call sits behind an interface with a Real and a Fake. Tests use Fakes only.
- Every path, service name, and GUID is in the `windows-verified-paths` skill, all of it under
  UNVERIFIED because none of it has been checked against Microsoft docs.
- No NuGet package was added. The only ones present are the xUnit test template's.
- Four safety guards were mutation-checked: undo ordering, undo continue-after-failure, the
  cleanup user-folder guard, and the services forbidden-list guard. Each one fails tests when
  disabled.
