# Progress

Working memory across context resets. Read this first, then `DECISIONS.md`, `BLOCKED.md`, and
`SESSION-REPORT.md`, then continue from **Next**.

## Session 5 — branding, theme, and a repository that was missing the app

**2026-07-28.** Intended as a branding and theme pass. It found that **the WPF app had never been
committed**: `.gitignore`'s `*.app` rule — the macOS bundle line from GitHub's template — matched
the directory `src/SystevoTune.App`, so all 39 files had been silently ignored since session 3. A
fresh clone could not build, and **CI had never passed once** in its seven recorded runs. Fixed,
and verified by cloning the repo fresh and building it.

Theme aligned to the Systevo mark (accent `#0070F3`, focus `#22D3EE`, both asserted against the
logo's own pixels). Splitting the accessibility tests by the WCAG criterion that actually applies
exposed a pre-existing defect: the button border was 1.52:1 against the card and the fill only
1.15:1, so a button at rest was nearly invisible. Buttons now use a separate 3:1 `ControlBorder`.

Build made deterministic: `global.json` pins the SDK to 8.0, the workflow reads it rather than
repeating the version, and the three CI actions moved off the deprecated Node 20 runtime.

**492 tests, 0 failures. CI green with zero annotations. Coverage 57.4%, unchanged.** The engine
was not touched — no tweak, path or whitelist entry changed. The app has still never been launched.

**The .NET 8 SDK was installed on this machine** at the maintainer's request, since the pin left a
9.0.314-only machine unable to build. See the standing-rules note at the bottom.

## Session 4 — blocked, no code changed

**2026-07-27.** The brief's VM results arrived as unfilled `<PASTE …>` placeholders, so Tier A
(triage and fix from the real-Windows run) could not start — see `BLOCKED.md` **B4**. Tier B of
that brief was session 3's Tier C, already complete; verified item by item rather than redone.

Produced: the B4 blocker write-up, `docs/VM-TRIAGE.md` as an empty ready-to-fill structure, and a
re-measured coverage figure (57.4% overall, 67.0% of testable logic). **No code changed.**

**Nothing further can be built until a VM run is reported.** The engine freeze was lifted for
session 4 and went unused, because the evidence to justify unfreezing never arrived.

## Where things stand

**All app and engine work is done; everything left needs a real machine.** Build clean, analyzers
on, zero warnings. **492 tests, 0 failures** (365 engine, 127 app). Coverage 57.4% overall.
**CI green**, and as of session 5 the repository actually contains the app. The app has never been
launched.

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

For the human. `VM-CHECKLIST.md` is the working copy; `SESSION-REPORT.md` section 6 summarises.

1. **VM snapshot, then `verify gaming --vm`.** Exit 0 + `PASS` satisfies doc 07.2.
2. **`docs/VM-CHECKLIST.md` steps 0–2.** Settles the 24 undocumented values.
3. **First manual click-through of all six screens.** The app has never been run. The one behaviour
   worth deliberately breaking to check: turn System Restore off in the VM and confirm the confirm
   dialog shows the warning in red and makes you read past it.
4. **`VM-CHECKLIST.md` section 6 — the theme.** New in session 5. Contrast is settled by tests, but
   how the lighter button border and the white-on-blue primary button actually *look* has never
   been seen by anyone.
5. **Open questions for the human** — `WSearch`, whether services tuning ships at all, B1 boot time
   (needs a decision on the `System.Diagnostics.EventLog` package), and which bloatware entries to
   approve. The Re-apply button is wired (`7d307b4`); earlier notes listing it as open were stale.

## For a resumed session

**There is no more build work queued.** Everything from all three tiers is done, and the next
meaningful step needs a real machine. Do not invent more features to fill the gap — the project's
risk is not "too few features", it is "24 Windows values nobody has checked".

**If a brief arrives citing VM results, check they are actually present before acting on them.**
Session 4's arrived as `<PASTE …>` placeholders. Triaging invented failures, or marking values
`VM-confirmed` without a run, would corrupt `windows-verified-paths` — the one file whose value is
that every line in it is true. Say it is blocked and stop; that is the correct outcome, not a
failure to try.

**Trust CI over a clean `git status`.** Session 5's `.gitignore` bug produced a clean status, green
local builds and accurate-sounding commit messages while the main deliverable was not in the repo
at all. The only thing that disagreed was the CI badge, and it had been red for two sessions
without anyone reading it. If a check is failing, find out why before doing anything else.

Known loose ends, if a session must do something:

- None in the app. The re-apply button was the last one and is wired (`7d307b4`).
- The engine stays **frozen** until the VM run: no new tweaks, paths or whitelist entries. Bug
  fixes with tests are fine.
- `docs/ARCHITECTURE.md` explains the layout and how to add a tweak, for whoever picks this up.

## Standing rules honoured, all five sessions

- **No system change has ever been applied to this machine.** `C:\ProgramData\SystevoTune` does not
  exist here; neither the ConsoleRunner nor the app has been executed; no tweak, service or
  registry value has been touched. Work here is build, test, git, file edits, and publish (build
  only).
  **One qualification, session 5:** the .NET 8 SDK was installed on this machine at the
  maintainer's request, because the new `global.json` pin left a 9.0.314-only machine unable to
  build. That is a development toolchain, not a tune-up action — but the rule used to read
  "nothing has ever run against this machine", and that phrasing is no longer exactly true. Stated
  rather than quietly dropped.
- Tests use Fakes only. All system calls sit behind interfaces.
- Every path, service name and GUID goes through `windows-verified-paths`, tiered into verified,
  undocumented, and closed questions.
- No NuGet package beyond the xUnit test template.
- Safety guards are mutation-checked — each is removed to confirm tests fail without it. Five in
  the engine; session 5 added two in the app theme (using the accent as text, and pointing buttons
  back at the decorative border).
- The repo builds at every commit — and as of session 5 that is verified against a **fresh clone**,
  not just this working copy. The two are not the same thing, which is how the app went two
  sessions without being committed.
