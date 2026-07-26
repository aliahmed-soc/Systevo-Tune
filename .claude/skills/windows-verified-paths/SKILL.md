---
name: windows-verified-paths
description: The only approved source of Windows registry paths, registry value names, service names, power plan GUIDs, and system folder paths for Systevo Tune. Use before writing or reviewing any code that names a registry key, a Windows service, a power scheme GUID, or a cleanup path.
---

# Verified Windows Paths

## The rule

**If a path is not in this file, stop and ask the user. Never guess.**

An invented registry key or service name looks exactly like a real one and can damage a
tester's PC. There is no acceptable workaround:

- Do not infer a path from a similar one already listed.
- Do not copy a path from memory, a blog post, or another optimizer tool.
- Do not "try it and see" — nothing is tested on the dev machine.
- Ask the user to verify it against Microsoft docs, then add it here with its source link.

## Status, 2026-07-27

A documentation pass covered every outstanding item. Three tiers now exist:

| Tier | Meaning | Count |
|---|---|---|
| **Verified** | Confirmed against Microsoft reference documentation | 12 |
| **Undocumented** | Microsoft does not document it. Community-sourced, needs a VM check | 15 |
| **Open question** | A real behavioural question the docs raised | 5 open, 1 resolved |

Nothing has run on a machine. "Verified" means the value matches Microsoft's own reference —
not that our use of it behaves as expected. The VM run is still required.

---

# Tier 1 — Verified against Microsoft documentation

## Power scheme GUIDs

Source: [Power Setting GUIDs (WinNT.h)](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids),
under `GUID_POWERSCHEME_PERSONALITY`. Verified 2026-07-27.

| # | Our id | Constant | GUID | Microsoft's description |
|---|---|---|---|---|
| V1 | `balanced` | `GUID_TYPICAL_POWER_SAVINGS` | `381B4222-F694-41F0-9685-FF5BB260DF2E` | "Automatic — balance performance and power consumption savings" |
| V2 | `high-performance` | `GUID_MIN_POWER_SAVINGS` | `8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C` | "High Performance — maximum performance at the expense of power consumption savings" |
| V3 | `power-saver` | `GUID_MAX_POWER_SAVINGS` | `A1841308-3541-4FAB-BC81-F71556F20B4A` | "Power Saver — maximum power consumption savings at the expense of system performance" |

`381B4222…` also appears throughout the
[powercfg documentation](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)
as the worked-example scheme GUID, and matches doc 05 section 5.2's own example. Note that
`GUID_MIN_POWER_SAVINGS` means *minimum power savings*, i.e. High Performance — the naming reads
backwards from what you would guess. **See open question O1** about personalities versus schemes.

## powercfg

Source: [Powercfg command-line options](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options).
Verified 2026-07-27.

| # | Purpose | Command |
|---|---|---|
| V4 | List power schemes | `powercfg /list` (alias `/L`) — "Lists all power schemes" |
| V5 | Switch active scheme | `powercfg /setactive <scheme_GUID>` (alias `/S`) |

The **output format of `/list` is not documented**. Our parser reads only the GUID and the
trailing `*`, never the labels, which is what keeps doc 07.4's non-English Windows case working.
That parsing choice is still empirical — see open question O2.

## Restore points

Source: [Checkpoint-Computer](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/checkpoint-computer).
Verified 2026-07-27.

| # | Item | Confirmed |
|---|---|---|
| V6 | The command | `Checkpoint-Computer -Description <string> [-RestorePointType <string>]` |
| V7 | `MODIFY_SETTINGS` is a valid type | Accepted values: `APPLICATION_INSTALL` (default), `APPLICATION_UNINSTALL`, `DEVICE_DRIVER_INSTALL`, `MODIFY_SETTINGS`, `CANCELLED_OPERATION` |
| V8 | The once-a-day limit is real | "Beginning in Windows 8, `Checkpoint-Computer` cannot create more than one system restore point each day." |
| V9 | The exact message we match | *"A new system restore point cannot be created because one has already been created within the past 24 hours. Please try again later."* |

Our two matched phrases — "already been created" and "within the past" — both appear in V9.
**In English only**; see open question O3.

Two constraints worth carrying:

- The cmdlet is **Windows PowerShell 5.1** (the doc carries no PowerShell 7 moniker). Our code
  invokes `powershell.exe`, not `pwsh.exe`, which is correct. Do not "modernise" that.
- Microsoft calls the 24-hour case an **error**, not a warning. Our code matches on the text
  before looking at the exit code, so it is handled either way — but the code comment claiming it
  "exits 0" was wrong and has been corrected.

## Native calls

| # | Call | Source | Confirmed |
|---|---|---|---|
| V10 | `GetSystemPowerStatus` / `SYSTEM_POWER_STATUS` | [winbase.h](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status) | Field order `BYTE ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; DWORD BatteryLifeTime, BatteryFullLifeTime`. `ACLineStatus` 0 = Offline, 1 = Online, 255 = Unknown. `BatteryFlag` 128 = "No system battery". Matches our struct exactly. |
| V11 | `GlobalMemoryStatusEx` / `MEMORYSTATUSEX` | [sysinfoapi.h](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex) | `DWORD dwLength, dwMemoryLoad; DWORDLONG ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual`. "You must set **dwLength** before calling" — we do. Matches our struct exactly. |

## Service start types

Source: [HKLM\SYSTEM\CurrentControlSet\Services Registry Tree](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/hklm-system-currentcontrolset-services-registry-tree).
Verified 2026-07-27.

| # | Purpose | Ref |
|---|---|---|
| V12 | Service start type | `HKLM\SYSTEM\CurrentControlSet\Services\<name>::Start`, REG_DWORD |

Documented values, matching our `ServiceStartType` enum exactly:

| Value | Meaning |
|---|---|
| `0` | Boot — loaded by the boot loader |
| `1` | System — loaded by the I/O subsystem |
| `2` | Automatic — started by the SCM during system startup |
| `3` | Demand (Manual) |
| `4` | Disabled |

## Group Policy registry values

| Purpose | Ref | Type | Source | Verified |
|---|---|---|---|---|
| Block Game Recording and Broadcasting | `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR::AllowGameDVR` | REG_DWORD | [ApplicationManagement Policy CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement) | 2026-07-27 |
| Diagnostic data level | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection::AllowTelemetry` | REG_DWORD | [System Policy CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system) | 2026-07-27 |

### AllowTelemetry — confirmed, and the catch that shapes our tweak

- **Values:** `0` Security, `1` Basic/required (**default**), `3` Full. There is no `2`.
- **The catch, in Microsoft's own words:** `0` "is only applicable to Windows 10 Enterprise,
  Windows 10 Education… **Using this setting on other devices is equivalent to setting the value
  of 1.**"
- So the engine writes **`1`, not `0`**, and the tweak is called *"Diagnostic data: required
  only"*. Writing `0` on a Home PC and calling it "telemetry off" would be a promise Windows does
  not keep — exactly the fake claim doc 01 rules out. Decision 38.
- **Scope:** Device *and* User. **Editions:** Pro, Enterprise, Education, IoT — **Home is not
  listed**, same caveat as `AllowGameDVR`.
- **Group Policy:** Computer and User Configuration → Windows Components → Data Collection and
  Preview Builds → "Allow Diagnostic Data". ADMX `DataCollection.admx`.
- Undo removes the value, returning Group Policy to "Not configured".

`0` = not allowed, `1` = allowed, **default `1`**. Device scope. Group Policy: Computer
Configuration → Windows Components → Windows Game Recording and Broadcasting, ADMX file
`GameDVR.admx`. Undo returns Group Policy to "Not configured", so there is no trap.

**Editions:** Pro, Enterprise, Education, IoT Enterprise. **Windows Home is absent from the
list** — and Home is a common gaming PC. Microsoft's own note adds "The policy is only enforced
in Windows 10 for desktop", which leaves Windows 11 behaviour worth confirming.

---

# Tier 2 — Undocumented by Microsoft

**Microsoft publishes no reference for any of these.** That is the finding, not a gap in
searching. Everything here comes from community, DFIR, or forum sources, which is better than
model guesswork but is *not* documentation. Each one needs an empirical check in the VM.

## Ultimate Performance power scheme

| # | Item | Value | Status |
|---|---|---|---|
| N1 | Ultimate Performance | `e9a42b02-d5df-448d-aa00-03f14749eb61` | **Not in Microsoft's documented GUID list.** Only three scheme personalities are documented (V1–V3). |

Low risk: the tweak reports `NotApplicable` when the scheme is absent, which is the common case
on consumer installs, and Gaming falls back to High Performance.

## Visual effects

| # | Ref | Type | Community-sourced meaning |
|---|---|---|---|
| N2 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects::VisualFXSetting` | DWORD | `1` best appearance, `2` best performance, `3` custom |
| N3 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize::EnableTransparency` | DWORD | `0` off |
| N4 | `HKCU\Control Panel\Desktop\WindowMetrics::MinAnimate` | **REG_SZ** | `"0"` off. A string, not a DWORD — the engine gets this right |

## Game Mode and Game Bar

| # | Ref | Type | Community-sourced meaning |
|---|---|---|---|
| N5 | `HKCU\Software\Microsoft\GameBar::AutoGameModeEnabled` | DWORD | `1` on, `0` off |
| N6 | `HKCU\System\GameConfigStore::GameDVR_Enabled` | DWORD | `0` off |
| N16 | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR::HistoricalCaptureEnabled` | DWORD | `0` off. The **"Record what happened"** background-recording toggle specifically — the precise lever doc 3.6 asks for |
| N17 | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR::AppCaptureEnabled` | DWORD | `0` off. **Broader than background recording** — also stops the user recording a clip by hand |

N16 and N17 were added 2026-07-27 on the user's instruction, resolving what was open question O6.
The scope difference between them is real and is why the tweak's display name dropped the word
"background" — see decision 30. Both are restored by undo.

## Start menu, tips and lock screen (doc 3.9)

All under `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager`, all REG_DWORD,
all set to `0`. Microsoft documents none of this key.

| # | Value | Community-sourced meaning |
|---|---|---|
| N18 | `SystemPaneSuggestionsEnabled` | App suggestions in the Start menu |
| N19 | `SubscribedContent-338388Enabled` | Start menu suggested content |
| N20 | `SilentInstalledAppsEnabled` | Auto-installing suggested apps. The one that puts software on the PC unasked |
| N21 | `SoftLandingEnabled` | "Get to know Windows" tips |
| N22 | `SubscribedContent-338389Enabled` | Tips and suggestions in Settings |
| N23 | `RotatingLockScreenOverlayEnabled` | Spotlight **overlay text** on the lock screen |

`RotatingLockScreenEnabled` — the Spotlight wallpaper itself — is deliberately **not** touched.
That is a picture the user chose, not an advert.

The `SubscribedContent-NNNNNN` numbers are opaque Microsoft content ids. They are the least
trustworthy entries in this file: they could change between Windows builds without notice, and a
stale id simply does nothing. **VM check: confirm each id still corresponds to the setting named.**

## GPU scheduling

| # | Ref | Type | Community-sourced meaning |
|---|---|---|---|
| N7 | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers::HwSchMode` | DWORD | `2` on, `1` off, **absent or `0` = let the system decide** |

The "absent means system default" detail matters and was not previously understood — see open
question O4.

## System Restore state detection

| # | Ref | Type | Community-sourced meaning |
|---|---|---|---|
| N8 | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore::RPSessionInterval` | DWORD | `0` = restore point creation off |
| N9 | `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore::DisableSR` | DWORD | `1` = disabled by policy. Group Policy "Turn off System Restore" under Computer Configuration → Administrative Templates → System → System Restore |

A sibling value `DisableConfig` ("Turn off Configuration") also exists — it stops the user
configuring System Restore without necessarily stopping restore points. We do not read it.
See open question O5 for a better approach than either.

## Startup approvals

Corrected 2026-07-27. Sources: [Windows Incident Response](http://windowsir.blogspot.com/2022/07/does-autostart-really-mean-autostart.html)
and Sysinternals [Autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns),
which reads these keys and is the closest thing to a Microsoft-published implementation.

| # | Purpose | Ref |
|---|---|---|
| N10 | Run, this user / all users | `HKCU` and `HKLM` `…\CurrentVersion\Run` (read only) |
| N11 | Approvals for Run | `…\CurrentVersion\Explorer\StartupApproved\Run` under the matching root |
| N12 | Approvals for folder items | `StartupApproved\StartupFolder` — **exists under both HKLM and HKCU** |
| N13 | 32-bit Run on 64-bit Windows | `HKLM\SOFTWARE\WOW6432Node\…\Run`, approvals at `StartupApproved\Run32` |

Value shape: REG_BINARY, 12 bytes — a 4-byte flag DWORD, then an 8-byte FILETIME recording when
the item was disabled.

| Flag byte | Meaning |
|---|---|
| `0x02` | Enabled |
| `0x06` | Enabled |
| `0x03` | Disabled |

The engine tests `byte0 & 0x01`, correct for all three, and errs toward "enabled" for an unknown
even flag — the safe side, since it means offering to disable something rather than believing it
is already off.

## Store package names (`Whitelists/bloatware.json`)

| # | Item | Status |
|---|---|---|
| N24 | The eight package names in the bloatware whitelist | Model knowledge, 2026-07-27. Confirm each with `Get-AppxPackage` in the VM before approving it. |

Every entry ships `approved: false` and the module builds no tweak for an unapproved entry, so a
wrong name currently removes nothing. A name that no longer exists is harmless; a wrong name that
matches something real is not — hence the approval gate.

`BloatwareWhitelist` refuses at load time anything containing a Windows-component fragment
(the Store, `SecHealthUI`, `VCLibs`, `NET.Native`, `UI.Xaml`, the shell hosts, `DesktopAppInstaller`).
Those break the PC in ways Undo cannot fix.

**Undo here is best effort and usually fails.** Re-registering needs the package files, which
removal generally deletes; after that only the Store can reinstall. This is the one undo path in
the engine that is expected to fail, and it says so in the preview and in the failure message.

## Cleanup paths

| # | Path | Note |
|---|---|---|
| N14 | `{USER_TEMP}`, `{WINDIR}\Temp` | Standard and uncontroversial, but no Microsoft page states "safe to empty" |
| N15 | `{SYSTEM_DRIVE}\$Recycle.Bin` | Per-SID bins. Undocumented layout. See decision 11 — `Clear-RecycleBin` is the shell-correct alternative |

`{WINDIR}\SoftwareDistribution\Download` **is** documented — and that verification found our
method wrong. See B3 in `docs/BLOCKED.md`.

---

# Tier 3 — Open questions

All six are now closed in code. What remains is empirical confirmation in the VM, which is what
`docs/VM-CHECKLIST.md` is for.

**O1 — CLOSED 2026-07-27 (decision 31).** Scheme personalities are not the same as schemes: an
OEM image can ship its own GUID that merely maps to a documented personality, and assuming the
GUID would have left such a PC on Balanced while reporting success. Schemes are now resolved at
runtime — exact GUID, then a scheme this engine created earlier, then by name. When nothing
matches, High Performance is copied from its template via the documented two-argument
`powercfg /duplicatescheme`, and undo deletes the copy. Ultimate Performance is deliberately
never invented. *VM check: `powercfg /list` on a real OEM laptop.*

**O2 — CLOSED 2026-07-27 (decision 35).** The active scheme now comes from the documented
`powercfg /getactivescheme` rather than a trailing `*` in undocumented `/list` output.

**O3 + O5 — CLOSED 2026-07-27 (decision 36).** The restore-point outcome is decided by counting
points before and after with the documented `Get-ComputerRestorePoint`, not by matching English
prose. Counting behaves identically on an Arabic Windows, which doc 07.4 requires. N8 and N9 drop
to a hint for the early warning; the counts are the authority.

**O4 — CLOSED 2026-07-27 (decision 37).** An absent `HwSchMode` stays `NotApplicable` and the
message now says the setting is simply not recorded, rather than claiming the hardware cannot do
it. Writing the value would be creating a setting rather than changing one. *VM check: whether
HAGS-capable machines at default settings really do lack the value.*

**O6 — CLOSED 2026-07-27 (decision 30).** Both Game Bar capture values added.

**O6 — RESOLVED 2026-07-27.** The user authorised adding the missing values, which is the
approval golden rule 5 asks for. Research then found **two** values rather than one, with
different scopes: `HistoricalCaptureEnabled` is background recording specifically, and
`AppCaptureEnabled` is all capture including manual clips. Both are now in the whitelist as N16
and N17. Still confirm in the VM that they behave as described.

---

## How to promote an entry

1. Find the official Microsoft documentation page.
2. Confirm the exact spelling, hive, value name, and value type.
3. Move the row into Tier 1 with its link and today's date.
4. An entry is only usable in code once it is committed here.

## Permanently forbidden — never add these

Out of scope by project rule, not by oversight. Do not add them to any table above.

- Windows Defender, any Defender-related service or policy key
- Windows Firewall services and rules
- Network stack services (DHCP, DNS Client, WLAN AutoConfig, Network List/Location)
- Audio services (Audiosrv, AudioEndpointBuilder)
- Print services (Spooler)
- Anything under a user profile folder: Documents, Desktop, Downloads, Pictures, Videos
