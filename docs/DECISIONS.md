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
| 23 | `windows-update-cache` removed from both profiles, kept in the whitelist | Verification found Microsoft's documented reset stops `bits`/`wuauserv`/`cryptsvc` before touching that folder, and renames rather than deletes. A staged update awaiting restart can have files that are unlocked but still needed, so locked-file handling does not protect us. No preset should do that blindly; ticking it deliberately still works. |
| 24 | All-users Startup folder approvals repaired from HKCU to HKLM | Verification found `StartupApproved\StartupFolder` exists under both hives, and the pairing follows the Run keys. The original assumption would have written to the wrong hive and silently done nothing. |
| 25 | Startup approval value writes a fresh FILETIME on disable, zeros on enable | The 8 bytes after the flag are the time the item was disabled. Carrying the old bytes across — the original behaviour — would stamp a re-disabled item with the time it was first disabled. |
| 26 | The 32-bit Run key (`WOW6432Node`) added as a startup location | Verification surfaced `StartupApproved\Run32`, which pairs with it. Without it, 32-bit startup entries on 64-bit Windows were invisible to the engine. |
| 27 | `AppCaptureEnabled` **not** added despite sources naming it | Golden rule 5 forbids adding an unverified path silently. It is logged as open question O6 for the VM instead. Setting only `GameDVR_Enabled` may not fully stop background capture — that is a known gap, deliberately left visible rather than papered over. |
| 28 | GPU scheduling's "not applicable" message reworded | An absent `HwSchMode` means "Windows is deciding", not "unsupported". The old message claimed knowledge we do not have. The conservative behaviour — never invent the value — is unchanged. |
| 29 | Kept `powershell.exe` rather than `pwsh.exe` for restore points | `Checkpoint-Computer` is documented for Windows PowerShell 5.1 only and is absent from PowerShell 7. This was right by accident; it is now right on purpose, and noted so nobody "modernises" it. |
| 30 | Game Bar tweak gained **two** values, and its display name dropped "background" | The user authorised adding `AppCaptureEnabled` (O6). Research then found `HistoricalCaptureEnabled` is the precise "Record what happened" lever doc 3.6 asks for, while `AppCaptureEnabled` is broader — it also stops the user recording a clip by hand. Both were added because the tweak was under-delivering without the first and the user asked for the second. The name had to change: calling it "background recording off" while it also kills manual capture would be the kind of quiet overreach doc 01 rules out. The id is unchanged so `gaming.json` still resolves. |

## Session 2 — 2026-07-27

### Human-decided (given to me at the start of session 2)

| # | Decision | Source |
|---|----------|--------|
| H1 | **B3 resolved.** Clear only `C:\Windows\SoftwareDistribution\Download`. Stop `wuauserv` and `bits` → delete contents → restart both. If either will not stop cleanly, skip the group with a warning result. Never delete while they run, never force-kill. No undo needed, but log file count and bytes freed. | Human, session 2 brief |
| H2 | This is the **only** exception to the services rule, and only inside that path. | Human, session 2 brief |
| H3 | No UI work this session except the optional WPF scaffold. | Human, session 2 brief |

### Made by me

| # | Decision | Why |
|---|----------|-----|
| 31 | Power schemes are matched at runtime by GUID, then by name; only High Performance may be created | Closes O1. Microsoft documents the three GUIDs as *personalities* every scheme "maps to", so an OEM image can ship its own id — assuming the GUID would leave the PC on Balanced while reporting success. Creation is limited to High: Ultimate Performance parks fewer cores and is a bigger change than a tune-up should invent on a machine that never offered it. Gaming therefore does Ultimate-if-present → High-if-present → create High. |
| 32 | A created scheme uses a fixed Systevo-owned GUID from the whitelist, not a powercfg-generated one | `powercfg /duplicatescheme` accepts a destination id (documented). Fixing it is what lets the change log name the scheme *before* it exists — log first, change second — and lets undo delete it by id. |
| 33 | Undo deletes any scheme the engine created | Doc 07.2 diffs the VM against its snapshot, so a leftover scheme is a bug. Undo's newest-first order does the switch-back before the delete, which matters because Windows refuses to delete the active scheme. |
| 34 | `FakePowerPlanService` now refuses to activate a scheme it does not hold | Mirrors powercfg. Without it, a bug that activates a scheme we failed to create would pass silently — and it did, until this fake was tightened. |
| 35 | **O2 closed.** The active scheme comes from `powercfg /getactivescheme`, not from a `*` in `/list` output | `/getactivescheme` is a documented option. `/list`'s output format is not, so reading a trailing asterisk was a guess about formatting. A GUID parses the same in every language. |
| 36 | **O3 + O5 closed.** The restore-point outcome is decided by counting points before and after with `Get-ComputerRestorePoint`, not by matching English prose | Doc 07.4 requires non-English Windows to work, and the old match was English-only. Counting is language-independent and uses a documented cmdlet. The registry values (N8, N9) drop to a hint for the early warning; the count is the authority. The phrase match survives only as a fallback when no counts come back at all. |
| 37 | **O4 closed.** An absent `HwSchMode` stays `NotApplicable`, and the message says why | Absent means Windows is choosing for itself — the default on a capable PC — so claiming "unsupported" would be a guess. Writing the value would be creating a setting rather than changing one, which golden rule 5 rules out. Detecting real HAGS support needs the driver's WDDM version, which is a VM question, not a code one. |
