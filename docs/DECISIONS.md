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
