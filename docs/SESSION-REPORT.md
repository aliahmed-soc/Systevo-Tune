# Session Report — session 5, 2026-07-28

**Build clean, zero warnings, analyzers on. 492 tests, 0 failures.**
**CI is green for the first time ever.** The app has still never been launched.

This session was meant to be a branding and theme pass. It found two things instead: the WPF app
had never been committed to the repository, and the button styling failed an accessibility bar
nobody had measured. Both had been true for two sessions.

---

## 1. The app was not in the repository

While committing a theme change I noticed `Dark.xaml` had no git history. It had none because
nothing under `src/SystevoTune.App` did:

```
$ git ls-files src/SystevoTune.App | wc -l
0
```

`.gitignore` line 427 is `*.app` — the macOS bundle rule out of GitHub's template. It matches the
**directory** `SystevoTune.App`. Every file of the WPF app, 39 of them, had been silently ignored
since session 3 created them. Both publish profiles were gone too, to `*.pubxml` on line 193,
which Visual Studio excludes because web profiles can carry database passwords.

Nothing warned about this. `git status` was clean, every commit succeeded, and the commit messages
describing the app were accurate about work that existed only on this disk.

**What it meant in practice:** a fresh clone got `SystevoTune.sln` pointing at a project that was
not there. It could not build at all. Neither could CI:

```
error MSB3202: The project file "…\src\SystevoTune.App\SystevoTune.App.csproj" was not found.
```

### CI had never once passed

Checking after the fix, GitHub had seven runs on record — every run since CI was introduced — and
the first five all failed at `Restore` on that error. The badge in the README had been red the
whole time. The durations give it away in hindsight: the failures took about a minute and died
before compiling, the passing runs take four.

`publish-portable` had never run for real either. It was publishing the console runner against a
solution missing the app, and reporting success for it.

### The fix

A documented block at the end of `.gitignore`, where the last matching rule wins. The directory
itself has to be re-included — git never descends into an excluded directory, so a rule naming the
files inside would never have been reached.

Verified rather than assumed: cloned the repo fresh into a scratch directory and built it there.
39 files present, build clean, 492 tests pass. Before the fix that clone produced nothing.

**The lesson worth keeping:** the repo was not the source of truth and nothing said so. Everything
that made the app look committed — clean status, green-looking local runs, detailed commit
messages — was consistent with the app being fully tracked. Only CI disagreed, and its badge was
being read as decoration rather than as a claim.

---

## 2. Brand alignment, and a contrast defect it exposed

The theme now takes its two colours from the Systevo mark itself: accent `#0070F3`, keyboard focus
`#22D3EE`. A test decodes `Assets/systevo-logo.png` and asserts both hexes really are among its
pixels, so the theme cannot drift from the brand without failing. Focus uses the *second* brand
colour rather than a thicker version of the first, so focus and hover differ by colour and not only
by border width.

Splitting the accessibility tests by the criterion that actually applies — 4.5:1 for text under
WCAG 1.4.3, 3:1 for control boundaries under 1.4.11 — then caught a real defect the old blanket
test had missed by only ever checking foreground colours:

| | ratio |
|---|---|
| card `#1E1F24` vs button fill `#282A31` | 1.15:1 |
| old shared border `#3A3D46` on the card | 1.52:1 |
| … on the button fill | 1.32:1 |

The button fill is nearly identical to the card behind it, so the border was the *only* thing
marking a button as a button — and at 1.52:1 it was close to invisible. This was not introduced by
the brand work; it had been in the theme since session 3 and shipped through a Tier C pass whose
stated job was accessibility.

Buttons now use a separate `ControlBorder` `#70737C`: 3.47:1 on the card, 3.02:1 on the fill. Cards
keep the quiet `#3A3D46`, because 1.4.11 exempts container decoration and pushing every card edge
to 3:1 would have made the UI shout. The primary button's label went white — near-black on the
brand blue is 4.11:1 and misses the bar that text on a fill has to clear; white is 4.55:1.

Five new tests. Three are mutation-checked: using `Accent` as a `Foreground` fails, and pointing
buttons back at the decorative border fails — that last one being the single way the split could
have been silently undone.

**None of this was looked at.** It is arithmetic on hex values. How it actually reads on a screen
is now section 6 of `VM-CHECKLIST.md`.

---

## 3. Build determinism

CI named a step "Set up .NET 8" and asked `setup-dotnet` for `8.0.x`, but with no `global.json`
the CLI takes the newest SDK installed — and the runner image ships 10.0.301. So every build,
including the green ones, was riding whatever Microsoft last put on the image and could change
without a commit.

Now pinned: `global.json` at `8.0.100` with `rollForward: latestFeature`, and `setup-dotnet` reads
that file instead of repeating the version, so there is one place to change it.

Two corrections came out of this, both worth recording because both were mine:

- **The first pin was malformed.** `"version": "8.0.0"` — the 8.0 SDK line starts at `8.0.100`, and
  `8.0.0` was never shipped. It passed CI anyway, because `setup-dotnet@v4` does not validate
  `global.json`. Bumping to v6, which does, rejected it immediately. A pin that looked right and
  was wrong survived exactly one run before a stricter tool caught it.
- **The obvious action bump was the wrong one.** Clearing the Node 20 deprecation warning looked
  like a move to `@v5`. Reading `action.yml` at each tag showed `upload-artifact@v5` still runs on
  `node20` — its own v6 notes say v5 "had preliminary support for Node.js 24, however this action
  was by default still running on Node.js 20". The version that looked like the fix was the one
  that would have left the warning in place. Gone to current majors instead: checkout v7,
  setup-dotnet v6, upload-artifact v7.

CI now runs on SDK 8.0.423 with zero annotations.

---

## 4. This machine changed, and the standing rule needs the asterisk

**The .NET 8 SDK was installed here**, at the maintainer's request, because the pin made the repo
unbuildable on a machine that had only 9.0.314. `winget install Microsoft.DotNet.SDK.8` → 8.0.423,
hash verified against Microsoft's signed build, with 9.0.314 left in place beside it.

Previous reports said "nothing has ever run against this machine." That is still true of the
**tune-up engine** — `C:\ProgramData\SystevoTune` does not exist here, and neither the app nor the
ConsoleRunner has ever been executed. But it is no longer true without qualification, and the
distinction is worth keeping sharp rather than quietly dropping: a development toolchain was
installed; no system tweak was applied.

The pin is repo-scoped. Inside the project `dotnet --version` gives 8.0.423; anywhere else on the
machine it still gives 9.0.314.

---

## 5. What this session produced

| | Commit |
|---|---|
| Systevo logo, app icon, copyright, brand strings in both languages | `9c6fc88` |
| `.gitignore` fix — the app enters the repository — plus the brand theme alignment | `16605d8` |
| README test count corrected, 417 → 492 | `2b57e65` |
| SDK requirement corrected in both languages | `cdd1f12` |
| `global.json` pin, workflow reads it, step renamed | `e8e78f4` |
| CI actions off the deprecated Node 20 runtime | `78dba4e` |
| `global.json` given a real SDK version | `babf9c6` |

Also: `DECISIONS.md` 40–42, `VM-CHECKLIST.md` section 6, and this report.

### Coverage

Re-measured: **57.4% overall**, 4181/7285 lines — unchanged from session 4. Expected, because this
session's product changes were XAML and configuration, which carry no measured lines. The
per-layer table in the previous report was produced ad hoc and is not reproduced here rather than
being restated from memory or recomputed under different groupings.

---

## 6. Still blocked, unchanged

**B4 stands.** No VM results have been supplied, so Tier A of the session-4 brief — triage and fix
from a real-Windows run — has still not started. `docs/VM-TRIAGE.md` remains an empty ready-to-fill
structure. Nothing in this session touched the engine, a tweak, a registry path or a whitelist.

The round-2 VM plan is unchanged and is not restated here. `VM-CHECKLIST.md` has it step by step,
which is the copy to actually work from; the session-4 report's prose version is in git history at
`6dfeb68` if it is wanted. Section 6 of that checklist is new, and covers the three theme changes
that need human eyes because contrast maths cannot judge them.

In short: snapshot the VM, run `verify gaming --vm` and `verify work --vm`, work the checklist
steps 0–2, click through all six screens in both languages, then paste the raw output into
`docs/VM-TRIAGE.md`.

---

## 7. Honest assessment

**The brief was cosmetic and the findings were not.** Aligning a colour is the kind of task that
is supposed to be safe. It surfaced a repository that had been missing its main deliverable for
two sessions and a UI control that was close to invisible — neither of which any amount of further
feature work would have revealed, and both of which had already survived a session whose explicit
job was polish.

**Two of my own mistakes were caught by tools, not by me.** The malformed SDK pin passed CI and was
rejected only when a stricter action looked at it. The `@v5` bump would have left the warning it
was meant to remove. Both were caught within minutes, which is the argument for pinning and
upgrading rather than for being more careful.

**The project's position has not moved.** 492 tests, clean build, CI green, and still nothing ever
run on Windows. What changed is that the repository now actually contains the software, and CI is
now a real signal rather than a red badge nobody was reading. The single highest-value action
available is still thirty minutes in a VM with a snapshot.
