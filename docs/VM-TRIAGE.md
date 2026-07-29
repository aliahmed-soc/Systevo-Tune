# VM Triage

> **Status: run in progress, 2026-07-29.** Read-only pass done on a real Windows 11 22H2 Pro VM.
> `verify` has not run yet, so nothing here is promoted to Tier 1. B4 is partially lifted.

This is where a real-Windows run gets turned into work. One line per failure, classified, *before*
any code is touched — the classification is what stops a wrong-scope bug being "fixed" by changing
a value, or a detection bug being "fixed" by changing a path.

## How to use it

1. Run the VM cycle (`docs/VM-CHECKLIST.md`).
2. Paste the raw output into **Run record** below. Raw, not summarised — the summary is what the
   triage produces, and starting from a summary loses the detail that decides the classification.
3. Fill the **Findings** table. One row per failure. Classify before fixing.
4. Only then start fixing. A fix is done when code, tests and the `windows-verified-paths` entry
   all agree.

## Classification

| Class | Means | Typical fix |
|---|---|---|
| **wrong path** | The key or folder does not exist as written | Correct the whitelist entry; re-check the docs |
| **wrong value** | Path is right, the data we write is not | Correct the value; check what Windows itself writes |
| **wrong scope** | Right name, wrong hive or context (HKCU vs HKLM, per-user vs machine) | Move it; check whether undo needs to move too |
| **works but detection wrong** | The change lands, but preview/status reports it incorrectly | Fix the read path, not the write path |
| **undo incomplete** | Applied fine, did not fully come back | The most serious class — the product promise is undo |

**Why the classes matter.** "Wrong scope" and "wrong path" look identical in a diff — both show a
value that did not change. Only the classification distinguishes moving a key from correcting one,
and getting that backwards produces a fix that passes its own test and still does nothing on a real
PC. That is precisely the failure mode N12 was corrected for in session 2, from reasoning alone.

## Run record

```
Date:                       2026-07-29
Windows version / edition:  10.0.22621 — Windows 11 22H2, Pro
                            (Get-ComputerInfo reports "Windows 10 Pro"; see note below)
Host:                       Hyper-V on CHILLZ, guest "Windows 11"
Build under test:           portable self-contained publish from CI run 30362829510 (commit a057a2d)
scan / startup / preview:   done
verify gaming --vm  → exit code: 0  PASS
verify work --vm    → exit code: 0  PASS  (9 changes, 0 failures, 9 restored, 2 permanent)
```

### verify gaming --vm — PASS, 2026-07-29 04:28

```
1. before      1 power scheme(s), 16 registry value(s), 2 service(s), 4 startup item(s), 6 watched package(s)
2. apply       19 change(s), 0 failure(s)
3. after-apply 17 difference(s) from the start - this is what the profile did
4. undo        17 restored, 0 failed, 2 permanent
5. after-undo  compared against step 1

PASS - the PC is exactly as it was before the run.
  Artifacts: C:\ProgramData\SystevoTune\verify\2026-07-29_04-28-09-gaming
```

19 changes = 17 undoable + 2 permanent. All 17 restored, 0 failed. The 2 permanent are the temp
files and Recycle Bin, reported as permanent rather than as failures — the `undoable: false`
handling behaving correctly against real files.

**The power-scheme create/delete path round-tripped.** `before` recorded 1 scheme; this VM offers no
High Performance plan, so the engine duplicated one, activated it, and on undo reactivated Balanced
and deleted the scheme it had created, returning to 1. This was the single riskiest undo path in the
engine and it worked unmodified on its first real run.

**What this does and does not prove.** It proves the Gaming profile applies and fully reverses on
Windows 11 22H2 Pro — reversibility, which is the product promise. It does **not** prove the tweaks
have their intended *effect*; a value can be written and reverted correctly while doing nothing
useful. Efficacy is what `VM-CHECKLIST.md` section 1 checks independently, and that is still to run.

**Edition note, and a checklist bug.** Step 0 asks for `(Get-ComputerInfo).WindowsProductName`,
which returned **"Windows 10 Pro"** on a machine whose build number is **22621 — Windows 11 22H2**.
That string comes from the registry's `ProductName`, which Microsoft never updated for Windows 11,
so it reads "Windows 10" on every Windows 11 machine. The build number is the reliable signal.
The edition half is right and is what matters here: **Pro**, so `AllowGameDVR` and `AllowTelemetry`
are both in scope and are expected to take effect. `VM-CHECKLIST.md` step 0 should lead with the
build number rather than the product name.

### Read-only observations worth keeping

- **Every registry path in the Gaming profile resolved and read back a plausible current value** —
  `VisualFXSetting` not set, `EnableTransparency` 1, `MinAnimate` "1", `GameDVR_Enabled` 1, the
  four `ContentDeliveryManager` values. No path errors. This is read-side evidence only; the write
  and undo paths are still unproven until `verify` runs.
- **HAGS reported `NotApplicable`** with the "no setting recorded" message — decision 37 behaving
  exactly as designed on hardware that has no such setting.
- **This VM has no High performance power scheme**, so the profile takes the *create* branch and
  undo has to remove a scheme it made. That is the riskiest undo path in the engine and it is
  good that it will be exercised rather than skipped.
- Cleanup found 1.3 GB in the Windows Update cache across 11,537 files — the U8 successor is
  finding real data, not an empty set.
- **The elevation interlock fired correctly.** `verify gaming --vm` from a non-elevated prompt
  refused with "Administrator rights are needed and this process does not have them. Nothing was
  changed." It reached that decision after reading state and before writing anything. First safety
  guard proven on real Windows rather than against a fake.
- **`preview work` detected the power plan as `[AlreadyApplied]`** — the VM is already on Balanced
  and the engine said so instead of re-applying it. The "already there, do nothing" branch works
  against a real `powercfg`, which the O2 rewrite depended on.
- Work profile plans 11 changes, Gaming 19. Both previews resolved every path.

**Sequencing note for the second verify.** Both profiles delete the same temp files, and that
deletion is permanent by design. After `verify gaming` runs, `verify work` will find fewer files to
clean — that is expected and is not drift. It still has 9+ registry changes, so it will not come
back `INCONCLUSIVE` for want of anything to do.

Paste `report.md` from `C:\ProgramData\SystevoTune\verify\<run>-<profile>\` for each profile.

## Findings

| # | Item | Symptom observed | Class | Fix | Status |
|---|---|---|---|---|---|
| V1 | `startup` lists a nameless entry | First row printed as `[on ]` with no name and no command. `StartupManager.ListRunItems` iterates `registry.GetValueNames(root, key)`, which includes the key's **default value — whose name is the empty string** — and yields it as a startup item. Harmless to list, but it is also offered as something to disable, and disabling it would write a junk approval entry keyed on `""`. | works but detection wrong | Skip empty-named values in `ListRunItems`. Read path only; the write path is not at fault. | open |
| V2 | `desktop.ini` listed as a startup program, twice | `StartupManager.ListFolderItems` enumerates *every* file in the Startup folders, so Explorer's `desktop.ini` metadata appears in both the per-user and all-users lists. Same consequence as V1: it is offered as disableable, and disabling it writes a bogus approval value. | works but detection wrong | Filter the Startup-folder enumeration to actual startup entries and exclude `desktop.ini`. | open |

| V3 | `SubscribedContent-338388Enabled` does not exist on this build | Section 1 read it back empty on Windows 11 22H2, while `-338389Enabled` is present and `1`. The engine creates 338388 from nothing and sets it to 0. Writing a value Windows does not use is not destructive and undo removes it cleanly, but we cannot claim the tweak does anything. This is exactly the risk the whitelist comment called "the least trustworthy entries". | wrong value (unconfirmed effect) | Do not guess. Identify the real id by toggling **Settings → Personalisation → Start → Show recommendations** and re-reading, then correct or drop the entry. | open |
| V4 | Three values documented as "present" are absent by default | `AutoGameModeEnabled` (N5), `HistoricalCaptureEnabled` (N16) and `AppCaptureEnabled` (N17) all read back empty on a clean install. The engine handles this correctly — preview showed `(not set) -> Dword:x` and undo removed them again — so this is a **documentation** defect, not a code one. The skill file claims they exist; they only appear once the user has touched the relevant Settings page. | works but detection wrong (docs) | Reword N5/N16/N17 in `windows-verified-paths` to "absent until first toggled". No code change. | open |
| V5 | ~~N12 unresolved~~ — **resolved, no defect** | A shortcut placed in the all-users Startup folder and disabled through Task Manager produced `ZZTest.lnk = {3,0,0,0…}` under **HKLM**, with HKCU still absent. The whitelist's HKLM pairing is correct, the value name keeps its `.lnk` extension as the code assumes, and the flag byte is `0x03` for disabled as documented. | — | None. Session 2's correction, made from reasoning alone, is confirmed right. | **closed** |
| V6 | The restore-point path was never exercised | `RPSessionInterval` is `0` and `Get-ComputerRestorePoint` returns 0 points: **System Restore is switched off on this VM.** Both `verify` runs therefore passed without ever creating a restore point, so that code path has still never run on real Windows. `Get-ComputerRestorePoint` itself returned cleanly, which does confirm O3/O5's counting method. | not yet testable | Turn System Restore on in System Protection and re-run one `verify`, then turn it off and confirm the app's red warning appears. | open |

| V7 | **The Startup feature misses most real startup items.** | Task Manager lists Microsoft 365 Copilot, SecurityHealthSystray, Terminal and Xbox. Our `startup` command found **only SecurityHealth**, plus a phantom (V1) and two `desktop.ini` files (V2). Terminal and Xbox are packaged apps whose startup state lives under `HKCU\Software\Classes\Local Settings\...\AppModel\SystemAppData\<family>\<task>`, and `StartupKind` has no such case — grepping the Engine for `StartupTask`/`AppModel`/`SystemAppData` returns nothing. Their "Disabled" status in Task Manager proves the approvals exist somewhere we never read. | wrong scope | Real engine work: add a packaged-startup-task location, or narrow the product claim. The path is undocumented and must go through `windows-verified-paths` first. | **open — largest finding of the run** |

| V8 | **The WPF app crashed on every launch. It had never once started.** | First launch produced `XamlParseException → IOException: Cannot locate resource 'assets/systevo.ico'` at `MainWindow.InitializeComponent()`. `MainWindow.xaml` sets `Icon="Assets/systevo.ico"`, but the csproj declared only the PNG as a `<Resource>`. `ApplicationIcon` is a different mechanism: it stamps the exe's Win32 icon and embeds nothing WPF can load. 100% reproducible, no workaround, the app was completely dead. | wrong scope | Added `<Resource Include="Assets\systevo.ico" />`. Verified in the compiled `SystevoTune.g.resources`, which now lists `systevo.ico` alongside `systevo-logo.png`. | **fixed** |

**Why nothing caught V8, which is the part worth learning from.** The app built clean, published
clean, passed CI twice, and passed nine dedicated branding tests. Every one of those tests asserted
that a *string* existed somewhere — `Icon="Assets/systevo.ico"` is in the XAML, `<ApplicationIcon>`
is in the csproj, the file is on disk, the PNG decodes. `The_logo_actually_decodes` even loaded the
image successfully, but through a **filesystem path**, never through the pack URI the app really
uses. Not one test asked whether the resource resolves at runtime, and no amount of static checking
of that kind could have found it. A single launch did, immediately.

`Every_asset_the_xaml_loads_is_embedded_as_a_resource` now compares what the XAML loads against
what the project embeds. Mutation-checked: removing the `<Resource>` line fails it.

The general lesson for this project, which has never run its own app: **green tests plus a green
build say nothing about whether the program starts.** Doc 07 should require a launch as an explicit
gate, not leave it implied.

**V7 has a concrete path now**, not a guess. Packaged startup tasks sit under
`HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<family>`,
confirmed on this VM to contain `Microsoft.WindowsTerminal_8wekyb3d8bbwe`,
`Microsoft.XboxGamingOverlay_8wekyb3d8bbwe` and five more Xbox families. It is still an
undocumented path and must enter `windows-verified-paths` as a new Tier 2 entry before any code
reads it.

**On V7's severity.** It does not endanger the undo promise: the engine cannot mis-handle what it
never touches, and both `verify` runs passed. What it breaks is the claim. `README.md` says Startup
"lists what starts with Windows and switches items off", and on a stock Windows 11 machine it found
one item in four. Either the feature grows to cover packaged apps or the sentence has to say what
it actually does. Per doc 01, overstating is the thing we do not do.

Both V1 and V2 are read-side defects found before any change was applied, which is the cheapest
place to find them. None of V1–V6 blocks `verify`, so the run continued and the fixes are batched
afterwards — fixing mid-run would mean rebuilding and re-copying the binary for no benefit.

## Items the run confirmed

Confirmed on Windows 11 22H2 Pro (build 22621), 2026-07-29, by reading through Windows' own tools
in section 1 rather than through our engine.

| Item | Evidence |
|---|---|
| **N4** — `MinAnimate` is a **string**, not a DWORD | `GetValueKind` returned `String`. This was the highest-risk type assumption in the whitelist: a DWORD here would have made undo restore the wrong kind of value, and `verify` could never have caught it because apply and undo share the assumption. |
| **O1** — Balanced GUID | `381b4222-f694-41f0-9685-ff5bb260df2e`, matching the documented value, and marked active. |
| **O2** — active-scheme detection | `/getactivescheme` agrees with the `*` in `/list`. The GUID-based rewrite works against a real `powercfg`. |
| **N1** — Ultimate Performance absent | Not listed, as expected on a consumer install. High Performance is absent too, which is why the create branch ran. |
| **N7 / O4** — `HwSchMode` absent | Read back empty, and the engine reported `NotApplicable` rather than claiming unsupported. Decision 37 holds. |
| **N3** — `EnableTransparency` | Present, `1`. |
| **N6** — `GameDVR_Enabled` | Present, `1`. |
| **N18–N22** — five ContentDeliveryManager values | `SystemPaneSuggestionsEnabled`, `SilentInstalledAppsEnabled`, `SoftLandingEnabled`, `RotatingLockScreenOverlayEnabled` all present and `1`. `SubscribedContent-338389Enabled` present and `1`. The sixth is V3. |
| **Spotlight wallpaper untouched** | `RotatingLockScreenEnabled` is present and `1`, and is not in any profile — decision 39 confirmed on a real machine. |
| **N12** — all-users startup approvals live in **HKLM** | The decisive result of this run. `ZZTest.lnk` appeared under `HKLM\…\StartupApproved\StartupFolder` after disabling it in Task Manager; HKCU stayed empty. Session 2 moved this from HKCU to HKLM on reasoning alone, and if that reasoning had been backwards the feature would have silently done nothing on every machine forever. Confirmed correct. |
| **N11** — approval value format | `ZZTest.lnk` is REG_BINARY beginning `03` for a disabled item, and the value name **includes the `.lnk` extension** — both exactly as `ReadState` and the approval-key logic assume. |
| **N13** — `StartupApproved\Run32` | Does **not** exist on this 64-bit VM. Absence is fine; the engine reads it optionally. |
| **N14 / N15** — cleanup paths | `%TEMP%`, `%WINDIR%\Temp` and `$Recycle.Bin` all exist. Update cache measured 1,403,192,089 bytes, matching `scan`'s 1.3 GB. |
| **V12** — service state parsing | `sc.exe query wuauserv` printed `STATE : 4 RUNNING` and `Start` is DWORD `3`. The parser reads the number, so it survives a localised Windows. |
| **O3 / O5** — restore-point counting | `Get-ComputerRestorePoint` returned a count without error. The counting method works; see V6 for why the count is 0. |
| **N24** — package names, 6 of 8 | `BingNews`, `BingWeather`, `GetHelp`, `Getstarted`, `MicrosoftSolitaireCollection`, `WindowsFeedbackHub` all present. `Microsoft3DViewer` and `MixedReality.Portal` absent — harmless, and all eight remain `approved: false`. |

**Not yet promoted to Tier 1.** The promotion also wants the manual checks that section 1 could not
script — N12's hive test above all — and V1–V6 should be resolved first so the file is edited once
rather than twice.

Each confirmed item moves from Tier 2 to Tier 1 in
`.claude/skills/windows-verified-paths/SKILL.md`, annotated `VM-confirmed <date>`. **That
annotation is only ever written from an observed run**, never from reasoning, however confident.

## Items still failing after the fix attempt

_Awaiting the first VM run._

Anything still broken after a genuine attempt goes to `BLOCKED.md` with the analysis and what to
check next time — not left half-fixed in the code.
