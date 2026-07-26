---
name: engine-conventions
description: C# code conventions for Systevo Tune — solution layout, the change-log record shape, result/error handling style, naming, and the xUnit test pattern. Use when adding or editing any file in SystevoTune.Engine, SystevoTune.ConsoleRunner, or SystevoTune.Engine.Tests.
---

# Engine Conventions

## Solution layout

```
src/SystevoTune.Engine          class library. All real logic. ZERO UI code.
  Whitelists/                   data files (JSON). Never inline a path or service name in code.
src/SystevoTune.ConsoleRunner   console app. Dev testing only. Never shipped to users.
tests/SystevoTune.Engine.Tests  xUnit. Pure logic only.
```

Later: `SystevoTune.App` (WPF) and `SystevoTune.Cli`. Both are thin — they call the Engine and
render its results. Any logic that appears in a front end is a bug; move it to the Engine.

**Zero UI code in the Engine** means: no `Console.WriteLine`, no `MessageBox`, no prompting, no
`Environment.Exit`. The Engine returns results and raises progress events. The caller decides
what the human sees.

Target framework is `net8.0-windows` across all three projects. `TreatWarningsAsErrors` is on
for Engine and ConsoleRunner — keep it that way.

## The change-log record

One record per change, written **before** the change runs. Shape is fixed by `docs/05-safety-layer.md`:

```json
{
  "id": "2026-07-26-001",
  "time": "2026-07-26T14:03:22",
  "module": "PowerPlan",
  "action": "SetActivePlan",
  "target": "ActivePowerScheme",
  "oldValue": "381b4222-f694-41f0-9685-ff5bb260df2e",
  "newValue": "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
  "undone": false
}
```

- One log file per run, in `C:\ProgramData\SystevoTune\logs`.
- The log is the single source of truth for undo. Nothing else records old values.
- `oldValue` must be captured from the live system, never assumed to be the Windows default.
- Serialize with `System.Text.Json` and camelCase names so the file matches the doc exactly.
- If a record cannot be written, the change does not run. No exceptions to this.

## Error handling style

Split failures into two kinds and treat them differently.

**Expected conditions → return a result, never throw.** Restore points disabled, a file locked,
a service already at the target value, not elevated. These are outcomes the user needs told
about, not crashes. Return a result object carrying success / warning / failure plus a message.

**Programmer errors → throw.** Null arguments, a whitelist file that does not parse, an unknown
enum value. These mean the build is wrong and must fail loudly in tests.

**Batch operations keep going.** Apply and undo both run over many items. One failed item never
stops the rest: catch it, record it, continue, and return the full list of failures at the end.
The caller reports them together. Never swallow a failure silently.

**Never leave a half-applied change.** If the log entry is written and the change then fails,
the record stays — undo must be able to find it.

## Naming

- Modules are named for what they touch: `CleanupModule`, `PowerPlanModule`, `ServicesModule`.
- Services that talk to Windows end in `Service`: `RestorePointService`.
- Every module exposes the same three verbs: `Scan`, `Preview`, `Apply` — plus `Undo` per item.
- Async methods end in `Async` and take a `CancellationToken`.
- Whitelist files are `Whitelists/<topic>.json`, lowercase topic.

## xUnit test pattern

- Test names read as sentences: `Undo_continues_after_one_step_fails`.
- Arrange / Act / Assert, with a blank line between the three. No comments needed.
- One behaviour per test. If the name needs "and", split it.
- File system tests use a temp directory created per test and deleted after — never
  `C:\ProgramData`. Inject the log directory path; do not hard-code it in the Engine.
- Anything that touches real system state is **not** unit tested. It is covered by the VM
  procedure in `docs/07-testing-plan.md`. Do not write a test that changes this machine.
- Every tweak ships with its undo test in the same commit.
