---
name: windows-verified-paths
description: The only approved source of Windows registry paths, registry value names, service names, power plan GUIDs, and system folder paths for Systevo Tune. Use before writing or reviewing any code that names a registry key, a Windows service, a power scheme GUID, or a cleanup path.
---

# Verified Windows Paths

This file holds **checked data only**. Every entry was confirmed against Microsoft
documentation and tested in a VM before being added here.

## The rule

**If a path is not in this file, stop and ask the user. Never guess.**

An invented registry key or service name looks exactly like a real one and can damage a
tester's PC. There is no acceptable workaround:

- Do not infer a path from a similar one already listed.
- Do not copy a path from memory, a blog post, or another optimizer tool.
- Do not "try it and see" — nothing is tested on the dev machine.
- Ask the user to verify it against Microsoft docs, then add it here with its source link.

## How to add an entry

1. Find the official Microsoft documentation page for the key, service, or GUID.
2. Confirm the exact spelling, hive, value name, and value type.
3. Ask the user to confirm before it is used in code.
4. Add a row below with the Microsoft docs link and the date verified.
5. The entry is only usable once it is committed here.

## Registry paths

| Purpose | Hive | Key path | Value name | Type | Docs | Verified |
|---|---|---|---|---|---|---|
| Block Game Recording and Broadcasting | HKLM | `SOFTWARE\Policies\Microsoft\Windows\GameDVR` | `AllowGameDVR` | REG_DWORD | [ApplicationManagement Policy CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement) | 2026-07-27 |

### AllowGameDVR — confirmed details

Was U22. Microsoft documents this exactly, as a Group Policy-backed MDM policy.

- **Values:** `0` = not allowed, `1` = allowed. **Default is `1`.**
- **Scope:** Device only (hence HKLM). Not a per-user policy.
- **Group Policy:** Computer Configuration → Windows Components → Windows Game Recording and
  Broadcasting → "Enables or disables Windows Game Recording and Broadcasting". ADMX file
  `GameDVR.admx`.
- **Editions:** Pro, Enterprise, Education, IoT Enterprise. **Windows Home is not listed.**
  Treat the tweak as possibly unenforced on Home — which is a common gaming PC edition.
- Microsoft's own note: "The policy is only enforced in Windows 10 for desktop." Confirm
  behaviour on Windows 11 during the VM run.
- **Reversibility is fine.** Setting it writes a policy value that greys out the user-facing
  Game Bar setting while present. Our undo restores the previous value, or deletes it when the
  tweak created it, which returns Group Policy to "Not configured". No trap.

## Service names

_None verified yet._

| Purpose | Service name | Display name | Default start type | Docs | Verified |
|---|---|---|---|---|---|

## Power plan GUIDs

_None verified yet._

| Plan | GUID | Docs | Verified |
|---|---|---|---|

## System folder paths (cleanup)

_None verified yet._

| Purpose | Path / known folder | Resolved via | Docs | Verified |
|---|---|---|---|---|

## UNVERIFIED — human must check before any VM run

Everything below came from the model's own knowledge during the autonomous session of
2026-07-26. **None of it has been confirmed against Microsoft documentation or tested.**
Code uses these values, and they are flagged here so they are checked before anything runs
on a real machine.

Checking one means: open the Microsoft docs page, confirm hive / path / value name / type,
then move the row into the verified tables above with its link and today's date.

### Registry values

| # | Purpose | Ref | Type | Assumed meaning | Used by |
|---|---|---|---|---|---|
| U1 | Is System Restore switched on | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore::RPSessionInterval` | DWORD | `0` = restore point creation off. Missing = default, treated as on. | `RestorePointService.IsSystemRestoreEnabled` |
| U2 | Is System Restore off by group policy | `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore::DisableSR` | DWORD | `1` = disabled machine-wide by policy. | `RestorePointService.IsSystemRestoreEnabled` |

### Commands

| # | Purpose | Command | Assumed behaviour | Used by |
|---|---|---|---|---|
| U3 | Create a restore point | `powershell.exe -NoProfile -NonInteractive -Command "Checkpoint-Computer -Description '<text>' -RestorePointType MODIFY_SETTINGS"` | Exit 0 on success. Emits a warning containing "already been created" / "within the past" when Windows declines because one was made in the last 24h. | `RestorePointService.CreateAsync` |

### Cleanup paths (`Whitelists/cleanup-paths.json`)

| # | Group | Path | Assumed meaning |
|---|---|---|---|
| U6 | temp-files | `{USER_TEMP}` → the user's `AppData\Local\Temp` | Safe to empty. Not one of the forbidden user folders (Documents / Desktop / Downloads). |
| U7 | temp-files | `{WINDIR}\Temp` | Machine-wide temp. Much of it is locked while Windows runs; the locked-file path handles that. |
| U8 | windows-update-cache | `{WINDIR}\SoftwareDistribution\Download` | **Path confirmed, procedure confirmed wrong — see below.** |
| U9 | recycle-bin | `{SYSTEM_DRIVE}\$Recycle.Bin` | Per-SID bins. Deleting the `$I`/`$R` files frees the space but may leave the shell's view stale until refresh. See decision 11 — `Clear-RecycleBin` is the shell-correct alternative to evaluate in the VM. |

#### U8 — checked 2026-07-27, and our approach was wrong

The **path is right**. Microsoft names `%Systemroot%\SoftwareDistribution\Download` in
[Additional resources for Windows Update](https://learn.microsoft.com/en-us/troubleshoot/windows-client/installing-updates-features-roles/additional-resources-for-windows-update).

The **procedure is not**. Microsoft's documented reset stops three services first:

```
net stop bits
net stop wuauserv
net stop cryptsvc
```

and then **renames** `Download` to `Download.bak` rather than deleting it — and only as an
escalation step, explicitly not the first thing to try. The lighter documented reset is
`net stop wuauserv`, `rd /s /q %systemroot%\SoftwareDistribution`, `net start wuauserv` — still
stopping the service first.

Our cleanup module deletes file-by-file with nothing stopped. Two consequences:

1. **Ineffective.** Most of the cache is locked while `wuauserv` and BITS run, so the freed size
   will fall well short of the scanned size. Doc 7.3 asks for "size shown ≈ size actually freed";
   this group would fail that check.
2. **Genuinely risky.** A staged update awaiting a restart may have files that are *not* locked
   but *are* still needed. Deleting those is how "deleting mid-update corrupts the install"
   happens. Locked-file handling does not protect against this case.

**Action taken:** `windows-update-cache` was removed from both the Gaming and Work profiles
(decision 23), so no preset touches it. The group stays in the whitelist and can still be ticked
deliberately, which matches doc 3.1's "user ticks what to clean".

**Still to decide (see BLOCKED.md B3):** whether to stop/start `wuauserv` + `bits` around the
delete, check for a pending restart first, or drop the group entirely.

### Power scheme GUIDs (`Whitelists/power-plans.json`)

Doc 05 section 5.2's worked example uses `381b4222… → 8c5e7fda…`, which matches U10 → U11.
That is corroboration from the project's own plan, not a Microsoft source — still check them.

| # | Plan | GUID |
|---|---|---|
| U10 | Balanced | `381b4222-f694-41f0-9685-ff5bb260df2e` |
| U11 | High performance | `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` |
| U12 | Ultimate performance | `e9a42b02-d5df-448d-aa00-03f14749eb61` |
| U13 | Power saver | `a1841308-3541-4fab-bc81-f71556f20b4a` |

### Registry tweak values (`Whitelists/registry-tweaks.json`)

| # | Tweak | Ref | Type | Assumed meaning |
|---|---|---|---|---|
| U17 | Visual effects | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects::VisualFXSetting` | DWORD | `1` best appearance, `2` best performance, `3` custom. |
| U18 | Visual effects | `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize::EnableTransparency` | DWORD | `0` off. |
| U19 | Visual effects | `HKCU\Control Panel\Desktop\WindowMetrics::MinAnimate` | REG_SZ | `"0"` off. Note it is a string, not a DWORD. |
| U20 | Game Mode | `HKCU\Software\Microsoft\GameBar::AutoGameModeEnabled` | DWORD | `1` on. |
| U21 | Game Bar recording | `HKCU\System\GameConfigStore::GameDVR_Enabled` | DWORD | `0` off. |
| ~~U22~~ | Game Bar recording | **VERIFIED 2026-07-27 — moved to the verified table at the top.** | | |
| U23 | GPU scheduling | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers::HwSchMode` | DWORD | `2` on, `1` off. Needs a restart (doc 3.6). Treated as NotApplicable when the value is absent, so the engine never invents it on unsupported hardware. |

Open questions for whoever verifies these:

- **U17–U19**: setting `VisualFXSetting` alone may not repaint until Explorer restarts or the user
  signs out. Check whether a `SystemParametersInfo` call is needed for the change to show
  immediately, and whether `UserPreferencesMask` also has to move.
- **U22** answered: it is the documented lever, it is device-scope by design, and undo returns
  Group Policy to "Not configured". The one caveat worth carrying forward is that Windows **Home**
  is absent from the supported-editions list.

### Startup locations (`Whitelists/startup-locations.json`)

The engine **never writes a Run value or deletes a shortcut**. It writes only the matching
`StartupApproved` value, which is the mechanism Task Manager uses, so Windows' own UI shows the
item as disabled rather than missing. Doc 3.2: disable, never delete.

**Microsoft does not document `StartupApproved` anywhere.** That is the finding, not a gap in
searching: it has no Policy CSP entry, no reference page, and no supported API. Everything below
comes from DFIR and Sysinternals-community sources, which is a step better than model guesswork
but is *not* the Microsoft confirmation these entries were waiting for. Treat U24–U28 as
**still unverified** and settle them empirically in the VM.

| # | Purpose | Ref | Status |
|---|---|---|---|
| U24 | Run, this user | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (read only) | uncontested |
| U25 | Run, all users | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` (read only) | uncontested |
| U26 | Approval flags for Run | `…\CurrentVersion\Explorer\StartupApproved\Run` under the matching root | uncontested |
| U27 | Approval flags for folder items | `StartupApproved\StartupFolder` **under both HKLM and HKCU** | **corrected 2026-07-27** |
| U28 | Startup folders | `{APPDATA}\…\Start Menu\Programs\Startup` and the `{PROGRAMDATA}` equivalent | uncontested |
| U31 | 32-bit Run on 64-bit Windows | `HKLM\SOFTWARE\WOW6432Node\…\Run`, approvals at `StartupApproved\Run32` | **added 2026-07-27** |

#### U27 — the original assumption was wrong

The whitelist paired **both** Startup folders with HKCU approvals. Sources agree
`StartupApproved\StartupFolder` exists under **HKLM as well as HKCU**, and the natural pairing
follows the Run keys: all-users location → HKLM approvals, per-user location → HKCU approvals.

Had this shipped, disabling an all-users Startup-folder item would have written to the wrong
hive and silently done nothing — the exact failure mode flagged as the risk. The whitelist now
pairs `{PROGRAMDATA}` with HKLM.

**Confirm in the VM:** put a shortcut in the all-users Startup folder, disable it in Task
Manager, and check which hive gained the value.

#### U26/U27 value shape — corrected

REG_BINARY, 12 bytes: a 4-byte flag DWORD, then an **8-byte FILETIME recording when the item was
disabled**.

| Flag byte | Meaning |
|---|---|
| `0x02` | Enabled |
| `0x06` | Enabled |
| `0x03` | Disabled |

The engine tests `byte0 & 0x01`, which gives the right answer for all three and errs toward
"enabled" for an unknown even flag — the safe side, since it means offering to disable something
rather than believing it is already off. A test now pins `0x06`.

The engine previously **carried the existing timestamp bytes across** when flipping the flag.
That was wrong: it would stamp a re-disabled item with the time it was first disabled. It now
writes a fresh FILETIME on disable and zeros on enable.

**Still open:** whether the approval key for a folder item includes the `.lnk` extension. The
engine assumes it does.

Source for the byte layout:
[Windows Incident Response — "Does Autostart Really Mean Autostart?"](http://windowsir.blogspot.com/2022/07/does-autostart-really-mean-autostart.html).
Sysinternals [Autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) reads
these keys and is the closest thing to a Microsoft-published implementation.

### Commands and native calls

| # | Purpose | Call | Assumed behaviour |
|---|---|---|---|
| U14 | List power schemes | `powercfg.exe /list` | Exit 0. One line per scheme containing its GUID; the active one ends with `*`. The parser reads only the GUID and the `*`, never the labels, so it survives a non-English Windows. |
| U15 | Switch power scheme | `powercfg.exe /setactive <guid>` | Exit 0 on success. |
| U16 | Mains or battery | `kernel32!GetSystemPowerStatus` | `AcLineStatus` 0 = on battery, 1 = plugged in, 255 = unknown. `BatteryFlag & 128` = no system battery. Struct layout in `SystemBatteryStatus`. |
| U29 | Installed and free RAM | `kernel32!GlobalMemoryStatusEx` | `MEMORYSTATUSEX` layout in `WindowsSystemMetrics`. `dwLength` must be set before the call. |
| U30 | Service start type | `HKLM\SYSTEM\CurrentControlSet\Services\<name>::Start` | DWORD. `0` boot, `1` system, `2` automatic, `3` manual, `4` disabled. Writing it changes the *next* boot and never stops a running service. |

### Output phrases matched as text

These are string matches against Windows' own messages, so they are locale-sensitive: on a
non-English Windows they will not match and the result falls through to `Failed`. Doc 07.4
lists non-English Windows as a required test case.

| # | Matched phrase | Treated as |
|---|---|---|
| U4 | "already been created", "within the past" | `Skipped` — a recent restore point exists |
| U5 | "system restore is disabled", "system protection is turned off", "0x81000203" | `Disabled` |

## Permanently forbidden — never add these

These are out of scope by project rule, not by oversight. Do not add them to any table above.

- Windows Defender, any Defender-related service or policy key
- Windows Firewall services and rules
- Network stack services (DHCP, DNS Client, WLAN AutoConfig, Network List/Location)
- Audio services (Audiosrv, AudioEndpointBuilder)
- Print services (Spooler)
- Anything under a user profile folder: Documents, Desktop, Downloads, Pictures, Videos
