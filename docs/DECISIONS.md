# Decisions

Choices made without the human present. One line each: what, and why.

| # | Decision | Why |
|---|----------|-----|
| 1 | Change log is JSONL (one JSON record per line), not a JSON array | Doc 5.2 needs the record on disk before the change and the log readable after a crash mid-change. Appending a line does both; appending inside an array corrupts the file if it dies mid-write. |
| 2 | Undo All spans every run, not only the last run | Doc 5.3 says last run. Applying twice would then strand run 1's changes with no way back, breaking "100% recoverable". Safe because every record stores an absolute old value, so newest-first lets the oldest record win. `UndoRunAsync` keeps last-run-only. |
| 3 | Projects target `net8.0-windows`, not plain `net8.0` | Registry and service APIs need the Windows TFM. .NET 8 runtime is installed; SDK 9 builds it. |
| 4 | Repo-local git identity `Systevo <278531835+aliahmed-soc@users.noreply.github.com>` | No global git identity was configured; a commit needs one. Global config untouched. |
| 5 | Restore points via PowerShell `Checkpoint-Computer`, not WMI | WMI would need the `System.Management` NuGet package. Doc 02 already allows PowerShell for this kind of work, so this adds no dependency. |
| 6 | `RestorePointService` never throws except on cancellation | Rule "never throw" is about restore being disabled, which is an expected condition. Swallowing `OperationCanceledException` would hide a hang from the caller, so it propagates and is documented. |
| 7 | Registry access is a hand-rolled `IRegistryService` over `Microsoft.Win32.Registry` | Registry APIs are in the `net8.0-windows` framework already — no package needed. The interface keeps tests off the real registry (rule 2). |
| 8 | `InternalsVisibleTo` the test project | Lets tests assert on verified paths and command building without widening the engine's public API. |
| 9 | `ChangeRecord` gains an `undoable` flag, default `true` | Cleanup deletions are genuinely permanent. Without this, Undo All would either report them as failures (wrong — nothing failed) or silently claim to restore them (dishonest). Default `true` keeps records written before the field readable. |
| 10 | Cleanup logs one record per group, not per file | Doc 3.1 shows the user size per group. A 10,000-row preview helps nobody, and the log stays small. The per-file detail is in the tweak's `LastApply` for the results screen. |
| 11 | Recycle Bin is treated as a file group over `$Recycle.Bin` | Keeps every cleanup group uniform and fully unit-testable now. `Clear-RecycleBin` is shell-correct and should be compared in the VM — logged as U9 in the verified-paths skill. |
| 12 | Whitelists ship as embedded resources | A user cannot redirect cleanup by dropping a JSON file next to the exe. Editing the repo copy and rebuilding is the only route. |
| 13 | Preview is enforced structurally, not by convention | `ITweak.PlanAsync` only reads; `ApplyChangeAsync` is called only by `TweakRunner`, which writes the log record first. A tweak has no code path to the system that skips the log. |
| 14 | Visual effects / Game Mode / Game Bar / GPU scheduling all run on one `RegistryTweak` driven by a JSON catalogue | Four tasks, one tested mechanism. Adding a tweak becomes a whitelist edit, not new code, which is also what rule 5 asks for. |
| 15 | `SystemBatteryStatus` uses `DllImport`, not `LibraryImport` | The `LibraryImport` source generator emits unsafe code, which would force `AllowUnsafeBlocks` across the whole engine for one read-only call. |
| 16 | Startup disable writes only `StartupApproved`, never the Run value | It is the mechanism Task Manager uses, so Windows' own UI agrees with us, and doc 3.2's "disable, never delete" holds by construction. It also means startup records are ordinary registry records, put back by the already-tested `RegistryUndoHandler`. |
| 17 | Startup items are not part of the Gaming/Work profiles | Which apps to cut is per-PC and per-person; doc 3.2 says "suggest", not "apply blindly". The engine exposes list + disable/enable for the UI to drive. Profiles carry only the fixed, safe tweaks. |
| 18 | `RegistryValueType.Binary` carries data as an uppercase hex string | The change log stores old and new values as single strings, so binary has to survive that round trip. Hex keeps the record readable by a human reading the log. |
| 19 | `apply` and `undo` in the ConsoleRunner refuse to run without an explicit `--vm` flag | The harness is only ever meant to run inside a throwaway VM. A mistyped command on a real desktop should do nothing at all. The flag is checked before the command starts, so a refused command has changed nothing. |
| 20 | The VM flag is reported before the missing-admin error | A user who mistypes `apply` on their own desktop should be told they are not in a VM, not sent to find an admin prompt and try again. |
| 21 | Command-line parsing and the guard live in a pure `CommandLine` type | It lets the interlock be unit tested. The ConsoleRunner itself is never executed on a dev machine, so anything only reachable by running it would be untested. |
| 22 | ConsoleRunner `apply` continues past a restore-point warning; the WPF app must not | Doc 5.1 says the user is asked. There is no user in a headless harness, and stopping would block the VM test. The message says so explicitly, and the app in Phase 3 has to stop and ask. |
