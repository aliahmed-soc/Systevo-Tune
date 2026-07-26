# Progress

Working memory for the autonomous session. If context resets, read this first,
then `DECISIONS.md` and `BLOCKED.md`, then continue from **Next**.

Session started: 2026-07-26. Human unavailable — decisions logged, not asked.

## Done

| # | Task | Commit | Tests |
|---|------|--------|-------|
| 0 | Solution, CLAUDE.md, three skills | `63b44ba` `cd272b2` | 1 |
| 1 | ChangeLog (JSONL, one file per run, log-before-change) | `8dc4f14` | 37 total |
| 2 | UndoEngine (Undo All / per-run / per-item, partial failure) | `8dc4f14` | 37 total |

## In progress

Task 3 — RestorePointService.

## Next

3. RestorePointService behind `IRestorePointService`. Disabled → warning result, never throw.
4. Dry-run framework: `ITweak.PlanAsync` = preview; `TweakRunner` = log-then-apply.
5. Cleanup module: scan first, whitelist file, locked files, never user folders.
6. Power plan switch + undo.
7. Visual effects toggle + undo.
8. Game Mode / Game Bar / GPU scheduling + undo (GPU sets needs-restart).
9. Startup manager: list + disable/enable, never delete.
10. Profiles: gaming.json, work.json through the same log/undo pipeline.
11. ConsoleRunner: scan → preview → apply → undo all.
12. If time: services engine support (empty whitelist), before/after metrics.

## Standing rules for this session

- Nothing runs against this machine. `dotnet build`, `dotnet test`, `git`, file edits only.
- Every system call sits behind an interface with a Real and a Fake. Tests use Fakes only.
- Every path / service name / GUID goes into the `windows-verified-paths` skill.
  Anything from model knowledge goes under **UNVERIFIED** there.
- A task is done only when build + tests are green, and it is committed.
