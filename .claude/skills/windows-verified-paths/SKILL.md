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

## Permanently forbidden — never add these

These are out of scope by project rule, not by oversight. Do not add them to any table above.

- Windows Defender, any Defender-related service or policy key
- Windows Firewall services and rules
- Network stack services (DHCP, DNS Client, WLAN AutoConfig, Network List/Location)
- Audio services (Audiosrv, AudioEndpointBuilder)
- Print services (Spooler)
- Anything under a user profile folder: Documents, Desktop, Downloads, Pictures, Videos
