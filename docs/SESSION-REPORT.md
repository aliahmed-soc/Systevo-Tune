# Session Report — autonomous build session 3, 2026-07-27

**Build clean, zero warnings, analyzers on. 461 tests, 0 failures.**
Nothing was launched. The app has never been run.

**All three tiers complete.**

---

## 1. Tiers completed

### Tier A — the WPF app

| | | Commit |
|---|---|---|
| A1 | `src/SystevoTune.App` (WPF, MVVM) + `tests/SystevoTune.App.Tests` | `5e0b272` |
| A2 | Screen 1 — Scan, read-only, sizes per cleanup group, state per tweak | `5e0b272` |
| A3 | Screen 2 — Review, tick list grouped by tweak, profile picker, select-all | `5e0b272` |
| A4 | Screen 3 — Apply, streams engine results live, restart flags aggregated | `5e0b272` |
| A5 | Screen 4 — Results, freed bytes, big Undo All, re-apply | `5e0b272` |
| A6 | Two-step apply with the restore-point warning in red | `5e0b272` |
| A7 | EN + AR resource files, FlowDirection, language switch, XAML literal scanner | `5e0b272` |
| A8 | Dark theme | `5e0b272` |
| A9 | 52 ViewModel tests across all six required cases | `5e0b272` |

### Tier B — hardening

| | | Commit |
|---|---|---|
| B1 | GitHub Actions CI on windows-latest, badge in README | `30faa9d` |
| B2 | Analyzers + `TreatWarningsAsErrors` everywhere | `30faa9d` |
| B3 | Log viewer screen | `153d1c3` |
| B4 | Settings screen | `153d1c3` |
| B5 | Portable single-file publish profiles, documented | `30faa9d` |
| B6 | `docs/SERVICES-WHITELIST-DRAFT.md` | `30faa9d` |
| B7 | Public README, EN + AR, honest status | `30faa9d` |

### Tier C — polish

| | | Commit |
|---|---|---|
| C1 | Keyboard navigation, tab order, Esc/Enter on dialogs | `89258af` |
| C2 | Accessibility names on every control, contrast computed | `89258af` |
| C3 | Empty and edge states, including the two that look alike | `89258af` |
| C4 | Idle RAM and startup app count shown as "before" on Scan | `89258af` |
| C5 | `docs/ARCHITECTURE.md` | `89258af` |
| C6 | Coverage measured and reported | `89258af` |

**C1/C2 are checked, not claimed.** The app cannot be launched here, so "we did an accessibility
pass" would be an assertion with nothing behind it. `AccessibilityTests` reads the XAML: every
interactive control has an `AutomationProperties.Name`, every screen sets a tab order, tab indexes
are unique per screen, the confirm dialog has both `IsDefault` and `IsCancel`, and Stop on the
apply screen is reachable with Esc. Contrast is computed from WCAG 2.1 relative luminance rather
than eyeballed.

Both scanners are guarded against passing vacuously — one asserts at least 15 controls were found,
and the automation-name check was mutation-tested by stripping one attribute (it failed, then
passed again once restored).

---

## 2. Test count and coverage

**461 tests: 365 engine, 96 app.** All green.

**Coverage: 55.5% overall.** That is the honest headline and the least useful number in this
report. Split by what can be tested at all:

| Layer | Coverage | |
|---|---|---|
| App view models + localization | **71.2%** | 585/822 lines |
| Engine logic | **64.2%** | 2963/4614 |
| **Testable logic combined** | **65.3%** | 3548/5436 |
| Windows adapters (`Platform/Windows/*`) | 8.8% | 52/592 — by design |
| ConsoleRunner (dev harness) | 4.6% | 21/461 |
| App views + composition root | 0% | XAML code-behind and `AppEngine.Create()` |

The three low rows are the layer that cannot be exercised without a real machine, which is exactly
what the VM run is for. They are kept as thin as possible so there is little in them to get wrong.

Per the brief I measured rather than chased. If you want the number moved, the honest place to
look is the ~35% of engine logic that is uncovered — largely guard clauses, exception branches and
display helpers, not decision-making. The decisions are covered: five safety guards are
mutation-checked and fail the suite when removed.

CI also uploads `coverage.cobertura.xml` as an artifact on every run.

---

## 3. Engine bugs found through UI work

Three, all fixed with tests.

**1. `Progress<T>` was the wrong tool, and it bit.**
It captures `SynchronizationContext` at construction and falls back to the **thread pool** when
there is none. The engine reports progress from inside `ConfigureAwait(false)` continuations, so
several callbacks landed on different pool threads at once and raced appending to an
`ObservableCollection` — which surfaced as a null element and a `NullReferenceException` in a
test. Replaced with `MarshalledProgress`, which posts to the UI context under WPF and runs inline
when there is none. The context is now passed explicitly, because xUnit installs its own context
and capturing "whatever is current" would have made the tests non-deterministic in a different way.

**2. `ApplyViewModel` leaked a `CancellationTokenSource` per run.** Caught by CA1001 once
analyzers were on. A fresh view model is created for every apply, so every run leaked one. Now
`IDisposable`, and the shell disposes the model it replaces.

**3. `ScanViewModel` used `FirstOrDefault` on an indexable collection.** Caught by CA1826. Trivial,
but it was a real allocation on a hot-ish path.

Two more things worth recording that were *not* engine bugs:

- The runner turning a throwing tweak into a `Blocked` plan is correct, and my test asserting it
  propagates was wrong. Split into two honest tests instead of changing the engine.
- A test asserting an unreadable log folder surfaces an error was untrue — `ChangeLog` treats a
  missing folder as empty, which is right for a PC where nothing has been applied. Replaced rather
  than forced to pass.

**One deliberate engine change, not a bug fix:** `TweakRunner.ApplyAsync` and
`ProfileApplier.ApplyAsync` gained an optional `IProgress<TweakOutcome>`. A4 requires streaming and
there was nothing to stream from. Additive, and nothing about a run changes when it is `null`. It
touches no tweak, path or whitelist, so the engine freeze holds.

---

## 4. Analyzer suppressions — please sanity-check these

B2 said no blanket suppressions and a reason for each. Fifteen findings: two were real defects
(above), thirteen were four rules fighting deliberate design decisions. They are switched off by
rule, individually, with the reasoning written into `Directory.Build.props` and
`tests/Directory.Build.props`:

| Rule | Why it is off |
|---|---|
| CA1859 | Wants concrete collection types returned. Returning `IReadOnly*` is what stops callers mutating collections the engine owns, and `ITweak` is an interface on purpose. |
| CA1720 | Objects to `RegistryValueType.String` and `PowerPlanEntry.Guid`. Those names *are* the JSON schema of the shipped whitelists. |
| CA1716 | Objects to `ITweak.Module`. That name is the change log's `module` field, fixed by doc 5.2. |
| CA1822 | Wants `TweakRunner` static. It is an injected service that will gain dependencies. |
| CA1707 (tests only) | Underscored test names are required by `engine-conventions` and are the most useful thing about the suite when something breaks. |
| CA1816 (tests only) | xUnit fixtures implement `IDisposable` purely to delete a temp folder. |

If you disagree with any of these, the fix is one line each — but the code change they imply is
larger, which is why I wrote the reasons down rather than just complying.

---

## 5. Open questions for you

1. **~~Coverage number~~ — measured, section 2.** 65.3% of testable logic, 55.5% overall.
2. **`WSearch` in the services draft.** It is the one candidate a user would actually notice —
   Start menu and Explorer search get worse. Arguably a feature, not bloat. My read is it should
   not go in a preset even if you approve it for manual use.
3. **Whether services tuning is worth shipping at all.** Working through B6 honestly: of ten
   candidates, three are safe *because* they are worthless, one (`SysMain`) has a real payoff and
   only on an SSD, and one is user-visible in a bad way. Doc 04 says ten solid features beat forty
   weak ones.
4. **B1 (boot time metric)** — still yours, unchanged from session 1. Needs a decision on the
   `System.Diagnostics.EventLog` package. My recommendation is still to drop it.
5. **Re-apply button on the Results screen** is present and enabled but not yet wired to an action
   — the command needs the same confirm-dialog path as Apply, which lives in the window rather
   than the view model. Small, but it is a visible control that currently does nothing. **This is
   the one loose end in the app**, and worth knowing before the click-through so you do not report
   it as a bug.

---

## 6. Tomorrow's VM plan

Unchanged in shape, with one screen-based step added at the end.

### 1. Snapshot the VM first

Nothing below is safe without it.

### 2. Run the automated cycle

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify gaming --vm
```

Snapshot → apply → snapshot → Undo All → snapshot → diff, in one command.
**Exit code 0 and `PASS`** means doc 07.2 is satisfied. Artifacts land in
`C:\ProgramData\SystevoTune\verify\<run>-<profile>\`.

Two results to read correctly: `INCONCLUSIVE` (exit 2) means the profile changed nothing so
nothing was proved — roll back and retry, do not treat it as a pass. Deleted temp files appearing
under "Permanent by design" is correct, not a failure.

Then repeat for `work`.

### 3. Work through `docs/VM-CHECKLIST.md`

Steps 0–2 are read-only and settle the 24 items Microsoft does not document. The three that carry
real risk are called out at the top: **N12** (all-users startup approvals possibly in the wrong
hive — disabling would silently do nothing), the **`SubscribedContent-NNNNNN` ids** (opaque, can
change between Windows builds), and **N24** (package names).

### 4. First manual click-through of the app

This is the new step, and the first time the app will ever have been launched.

```bash
dotnet run --project src/SystevoTune.App
```

It requests admin at start (`app.manifest`), so expect a UAC prompt. Walk all four screens:

- **Scan** — do the sizes and current states match what step 3 showed you by hand?
- **Review** — tick and untick; does the count follow? Is the permanent-deletion warning on the
  cleanup rows?
- **Apply** — the confirm dialog must name the restore point. If System Restore is off in the VM,
  **the warning must be red and you must have to read past it.** That is A6, and it is the one
  piece of UI behaviour worth deliberately breaking to check.
- **Results** — freed bytes plausible? Then press **Undo All** and confirm the machine comes back.
- **Logs and Settings** — do the runs you just made appear? Does switching to Arabic mirror the
  whole window, not just the text?

Anything that looks wrong is a UI bug, not an engine bug: the engine path underneath was already
proved by step 2.

---

## 7. Honest assessment

**The app is written but unproven in a way the engine no longer is.** Every ViewModel decision is
tested, and the XAML is deliberately plain because I could not look at it — no custom controls, no
animation, no layout that needs an eye. What I cannot tell you is whether it *looks* acceptable,
whether the Arabic layout mirrors cleanly, or whether anything is clipped at 100% scale. Those are
step 4 above, and I would expect to find several small things.

**The three bugs UI work surfaced are the argument for having done it.** None of them would have
appeared from more engine tests: they came from putting the engine behind something that consumes
it concurrently, holds state across screens, and gets compiled with analyzers on.

**Tier C is thinner than A and B, and should be.** Most of it is verification of things already
built rather than new capability — which is why it fits in the gap after the main work rather than
competing with it. The part worth keeping is the two XAML scanners: they turn "we did an
accessibility pass" from a claim into something that fails the build when it stops being true.
Everything in C1/C2 is a check on code that already existed.

**What has not moved:** the 24 undocumented Windows values are exactly where session 2 left them.
No amount of app work touches that, and the VM checklist is still the only route. Three sessions
of building have not brought the project one step closer to knowing whether
`SubscribedContent-338388Enabled` means what we think it means — only the VM can answer that, and
until it does, this is well-tested software that may be aimed at some wrong targets.
