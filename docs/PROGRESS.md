# Progress

Working memory across context resets. Read this first, then `DECISIONS.md`, `BLOCKED.md`, and
`SESSION-REPORT.md`, then continue from **Next**.

## Where things stand

**Session 2 complete, 2026-07-27.** Build clean, 0 warnings, **365 tests, 0 failures.**
Nothing has ever run against the dev machine.

## Session 2 — done

| # | Task | Commit |
|---|------|--------|
| 1 | O1 closed — runtime scheme resolution, reversible creation | `c2aa4db` |
| 2 | O2–O5 closed | `c280620` |
| 3 | B3 implemented per decision H1 | `4994a55` |
| 4 | Session-1 leftovers — only B1 open, needs the human | — |
| 5 | Privacy module | `6542392` |
| 6 | Bloatware remover engine | `b222333` |
| 7 | Re-apply last profile | `708ac0f` |
| 8 | VM verification harness + `docs/VM-CHECKLIST.md` | `0a5c9a5` |
| 9 | WPF scaffold — **not started**, optional, out of session time | — |

## Session 1 — done

Tasks 0–12: solution, change log, undo engine, restore points, dry-run framework, cleanup, power
plan, registry tweaks, startup manager, profiles, ConsoleRunner, services support, metrics.
Then a full documentation pass over every Windows path.

## Next

For the human, in order. Full detail in `SESSION-REPORT.md` section 6.

1. **Review `DECISIONS.md` 31–39.** Especially 31 (Ultimate is never created), 38 (telemetry
   writes `1`, not `0`), 39 (Spotlight wallpaper left alone), 23 (update cache still out of both
   profiles).
2. **`docs/VM-CHECKLIST.md` steps 0–2.** All read-only.
3. **VM snapshot, then `verify gaming --vm`.** Exit 0 + `PASS` is doc 07.2 satisfied.
4. Optional: fill `services.json`, approve bloatware entries, decide B1.

## For a resumed session

- Task 9 (WPF scaffold) is the only unstarted item. Plain controls, MVVM, EN + AR resources,
  zero visual design. Everything it needs from the Engine already exists — see `EngineHost` in
  the ConsoleRunner for how the pieces wire together.
- B1 (boot time) needs a human decision on a NuGet package. Do not add it unasked.
- 24 values remain undocumented by Microsoft (N1–N24). They cannot be closed by research; the
  VM checklist is the only route.

## Standing rules honoured, both sessions

- Nothing ran against this machine. `dotnet build`, `dotnet test`, `git`, file edits only.
  `C:\ProgramData\SystevoTune` does not exist here; the ConsoleRunner has never been executed.
- Every system call sits behind an interface with a Real and a Fake. Tests use Fakes only.
- Every path, service name and GUID goes through `windows-verified-paths`, tiered into verified,
  undocumented, and closed questions.
- No NuGet package beyond the xUnit test template.
- Five safety guards are mutation-checked — each fails tests when removed.
- The repo builds at every commit.
