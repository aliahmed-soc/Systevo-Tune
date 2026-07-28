# Progress

Working memory across context resets. Read this first, then `DECISIONS.md`, `BLOCKED.md`, and
`SESSION-REPORT.md`, then continue from **Next**.

## Where things stand

**Session 3 complete, 2026-07-27. All three tiers done.** Build clean, analyzers on, zero warnings.
**461 tests, 0 failures** (365 engine, 96 app). Coverage 65.3% of testable logic, 55.5% overall.
The app has never been launched.

## Session 3 — Tiers A, B and C

| Tier | Item | Commit |
|---|---|---|
| — | Shared test doubles extracted to `SystevoTune.TestSupport` | `e49f651` |
| A | WPF app, four screens, EN/AR, dark theme, 52 ViewModel tests | `5e0b272` |
| B | CI, analyzers, portable publish, README, services research | `30faa9d` |
| B | Log viewer and settings screens | `153d1c3` |
| C | Metrics on Scan, edge states, accessibility checks, architecture doc, coverage | `89258af` |

## Earlier sessions

- **Session 1:** engine — change log, undo, restore points, dry-run framework, cleanup, power
  plan, registry tweaks, startup, profiles, ConsoleRunner, services support, metrics.
- **Session 2:** closed O1–O6, implemented B3 (update cache), privacy, bloatware, re-apply, and
  the VM verification harness + checklist.

## Next

For the human. Full detail in `SESSION-REPORT.md` sections 5 and 6.

1. **VM snapshot, then `verify gaming --vm`.** Exit 0 + `PASS` satisfies doc 07.2.
2. **`docs/VM-CHECKLIST.md` steps 0–2.** Settles the 24 undocumented values.
3. **First manual click-through of all four screens.** The app has never been run. The one
   behaviour worth deliberately breaking to check: turn System Restore off in the VM and confirm
   the confirm dialog shows the warning in red and makes you read past it.
4. **Five open questions** in `SESSION-REPORT.md` section 5 — coverage number, `WSearch`, whether
   services tuning ships at all, B1 boot time, and the unwired Re-apply button.

## For a resumed session

**There is no more build work queued.** Everything from all three tiers is done, and the next
meaningful step needs a real machine. Do not invent more features to fill the gap — the project's
risk is not "too few features", it is "24 Windows values nobody has checked".

Known loose ends, if a session must do something:

- The **Re-apply button on Results** is enabled but not wired — it needs the confirm-dialog path,
  which lives in `MainWindow` rather than the view model. The one visible control that does
  nothing.
- The engine stays **frozen** until the VM run: no new tweaks, paths or whitelist entries. Bug
  fixes with tests are fine.
- `docs/ARCHITECTURE.md` explains the layout and how to add a tweak, for whoever picks this up.

## Standing rules honoured, all three sessions

- Nothing has ever run against this machine. Build, test, git, file edits, and one publish (build
  only). `C:\ProgramData\SystevoTune` does not exist here; neither the ConsoleRunner nor the app
  has been executed.
- Tests use Fakes only. All system calls sit behind interfaces.
- Every path, service name and GUID goes through `windows-verified-paths`, tiered into verified,
  undocumented, and closed questions.
- No NuGet package beyond the xUnit test template.
- Five safety guards are mutation-checked — each fails tests when removed.
- The repo builds at every commit.
