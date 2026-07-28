# Architecture

One page. If you are adding a tweak, skip to [Adding a new tweak](#adding-a-new-tweak).

## Solution map

```
SystevoTune.sln
│
├── src/SystevoTune.Engine ........... all the real logic. ZERO UI code.
│   ├── Safety/ ...................... change log, undo engine, restore points
│   ├── Platform/ .................... interfaces for everything that touches Windows
│   │   └── Windows/ ................. the only classes that call real Windows APIs
│   ├── Tweaks/ ...................... ITweak, TweakRunner, and the tweak families
│   ├── Cleanup/ ..................... scan-first file deletion
│   ├── Startup/ ..................... list and disable startup items
│   ├── Bloatware/ ................... Store app removal
│   ├── Profiles/ .................... Gaming/Work presets, apply, re-apply
│   ├── Metrics/ ..................... before/after numbers
│   ├── Verification/ ................ the doc 07.2 harness
│   ├── Whitelists/ .................. DATA. Every path the engine may touch.
│   └── Profiles/*.json .............. DATA. The presets.
│
├── src/SystevoTune.App .............. WPF, MVVM. Calls the Engine, duplicates none of it.
│   ├── ViewModels/ .................. all the logic. Testable with no UI thread.
│   ├── Views/ ....................... XAML. Thin on purpose.
│   ├── Localization/ ................ ILocalizer + en.json / ar.json
│   └── Services/AppEngine.cs ........ composition root
│
├── src/SystevoTune.ConsoleRunner .... dev harness. Holds `verify`. Never shipped to users.
│
└── tests/
    ├── SystevoTune.Engine.Tests ..... 365 tests
    ├── SystevoTune.App.Tests ........ 96 tests
    └── SystevoTune.TestSupport ...... the Fakes, shared so they cannot drift
```

**The rule that shapes all of it:** the Engine has no UI, and the App has no logic. Anything that
decides *what to change* lives in the Engine. Anything that decides *how it looks* lives in the
App. If you find yourself writing an `if` in XAML, it belongs in a view model; if you find
yourself writing a message in a view model, it probably belongs in a resource file.

## The log-first / undo flow

This is the whole product. Everything else is detail.

```
   USER PICKS A PROFILE
            │
            ▼
   ┌─────────────────────┐
   │  ITweak.PlanAsync   │   READS ONLY. This is the entire dry run.
   │  (per tweak)        │   Returns: old value → new value, or why it will not run.
   └─────────────────────┘
            │
            ▼
      review screen           user ticks what they want. NOTHING HAS CHANGED.
            │
            ▼
      confirm dialog          restore point attempted HERE, before any tweak runs.
            │                 not "Created"? red warning, must be read past.
            ▼
   ┌─────────────────────────────────────────────────┐
   │  TweakRunner.ApplyAsync  — for each change:      │
   │                                                  │
   │    1. run.RecordChange(...)   ── WRITE THE LOG   │
   │           │                       (old value     │
   │           │                        included)     │
   │           ▼                                      │
   │       ✓ on disk                                  │
   │           │                                      │
   │    2. tweak.ApplyChangeAsync(...)  ── CHANGE IT  │
   │                                                  │
   │    a failure here is collected, not fatal;       │
   │    the run continues and reports at the end      │
   └─────────────────────────────────────────────────┘
            │
            ▼
   C:\ProgramData\SystevoTune\logs\run-<timestamp>.jsonl
            │
            │   one JSON record per line, append-only
            │   { id, time, module, action, target, oldValue, newValue, undone, undoable }
            │
            ▼
   ┌─────────────────────────────────────────────────┐
   │  UndoEngine.UndoAllAsync  — NEWEST FIRST         │
   │                                                  │
   │    for each record not yet undone:               │
   │      undoable: false?  → listed as permanent,    │
   │                          never as a failure      │
   │      no handler?       → reported, not thrown    │
   │      otherwise         → IUndoHandler restores   │
   │                          the ABSOLUTE old value  │
   │      then mark undone on disk                    │
   └─────────────────────────────────────────────────┘
```

**Why the order matters.** Log first means a crash mid-change still leaves an undo path. Newest
first, with absolute old values, means the oldest record always has the last word — so applying
Gaming then Work then Undo All lands on the user's original settings, not on either preset.

**Why JSONL and not a JSON array.** A record has to reach disk *before* the change runs. Appending
a line does that; appending inside an array means rewriting a closing bracket, and a crash there
corrupts the whole file. A torn line costs one record, and the reader counts it rather than
throwing.

## Where the whitelists live

`src/SystevoTune.Engine/Whitelists/*.json`, embedded as resources so a user cannot redirect the
app by dropping a file next to the exe.

| File | Holds | Guard |
|---|---|---|
| `cleanup-paths.json` | Folders cleanup may empty | Refuses Documents, Desktop, Downloads, Pictures, Videos, Music, the profile root, the Windows folder itself, and bare drive roots |
| `registry-tweaks.json` | Every registry value the engine may write | Roots validated at load |
| `power-plans.json` | Power scheme GUIDs | — |
| `startup-locations.json` | Run keys and Startup folders | Writes only `StartupApproved`, never a Run value |
| `services.json` | Services that may be retuned. **Ships empty.** | Refuses Defender, firewall, network, audio, printing, sign-in services, and driver start types |
| `bloatware.json` | Store apps that may be removed. **All `approved: false`.** | Refuses the Store, Windows Security, framework packages, shell hosts |

Every guard is enforced **at load time**, not at use time, and each one is mutation-tested — the
test suite fails if the guard is removed. That is deliberate: a whitelist file is data, and data
gets edited by people in a hurry.

`Whitelists/` is also the answer to "where do I look up a path?" — nothing is hard-coded in C#.
Anything not in a whitelist is not in the product, and anything not yet checked against Microsoft
docs is tracked in [`windows-verified-paths`](../.claude/skills/windows-verified-paths/SKILL.md).

## Adding a new tweak

Most tweaks need **no C# at all.**

### If it is a registry value

1. Add an entry to `Whitelists/registry-tweaks.json` — id, English and Arabic names, and the
   values to write.
2. Add the path to the `windows-verified-paths` skill. If Microsoft documents it, Tier 1 with the
   link; if not, Tier 2 with a note. **Never add a path you have not looked up.**
3. Add it to `Profiles/gaming.json` and/or `work.json` if a preset should include it.
4. Add a test. The existing ones in `RegistryTweakTests` are the pattern.

That is it. `RegistryTweak` handles preview, apply and undo; `RegistryUndoHandler` restores the
previous value, or deletes the value if the tweak created it.

### If it needs new behaviour

1. Implement `ITweak`:
   - `PlanAsync` **reads only** and returns what would change. This is the dry run — if it writes
     anything, preview is broken.
   - `ApplyChangeAsync` applies exactly one already-planned change. It is only ever called by
     `TweakRunner`, which writes the log record first. There is no other path to the system.
2. Implement `IUndoHandler` for the module, unless the change is genuinely permanent — in which
   case plan it with `Undoable: false` and it is reported as permanent instead of failed.
3. Register the handler in `AppEngine.Create()` **and** `EngineHost.Create()`. Forgetting one is
   how "no undo handler is registered" reaches a user; it has happened once already.
4. Put every Windows call behind an interface in `Platform/`, with a Fake in `TestSupport`.
   **Tests never touch the real machine.**
5. Add the tweak's values to the whitelist and the skill, as above.

### The four rules a new tweak must not break

1. **Log before change.** Enforced structurally — you would have to add a new caller of
   `ApplyChangeAsync` to break it.
2. **Whitelists only.** No paths in C#.
3. **An undo path, or an honest `Undoable: false`.** No third option.
4. **Never** Defender, firewall, network, audio, printing, or user folders. The one exception is
   the update-cache cleanup stopping `wuauserv`/`bits`, which is scoped and enforced in
   `CleanupWhitelist`.

## Testing shape

- **Engine and view models:** unit tested against Fakes. No registry, no processes, no disk outside
  a temp folder.
- **`Platform/Windows/*`:** deliberately untested here — it is the layer that cannot be exercised
  without a real machine. Kept as thin as possible so there is little to get wrong.
- **XAML:** checked by tests that read the files (no hard-coded strings, every interactive control
  named, tab order set, contrast above WCAG AA) rather than by rendering it.
- **The machine itself:** `docs/VM-CHECKLIST.md` and
  `ConsoleRunner verify <profile> --vm`, which runs snapshot → apply → undo → diff and fails on
  any difference.
