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

_None verified yet._

| Purpose | Hive | Key path | Value name | Type | Docs | Verified |
|---|---|---|---|---|---|---|

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
