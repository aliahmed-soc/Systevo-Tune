---
name: windows-tweak-safety
description: Safety rules for any code that reads or changes Windows system state — registry, services, power plans, startup items, scheduled tasks, or app removal. Use when writing, editing, or reviewing any file in Optimizer.Engine, or any PowerShell called from it.
---

# Windows Tweak Safety Rules

Follow ALL rules. If a rule blocks the task, stop and tell the user instead of working around it.

1. Log first. Write the change-log JSON entry (old value included) BEFORE applying any change.
2. Whitelist only. Services, bloatware, and cleanup paths must come from the whitelist files in /Optimizer.Engine/Whitelists. Never add items inline in code.
3. Undo required. Every new tweak ships with its undo method and a test for it in the same commit.
4. Verified paths only. Registry paths, service names, and GUIDs must come from the windows-verified-paths skill. If missing there, ask the user to verify — do not guess.
5. Forbidden targets: Windows Defender, firewall, network, audio, printer services; any file under user folders (Documents, Desktop, Downloads).
6. Dry-run support. Every tweak must work in preview mode (report the change, apply nothing).
7. Admin checks. Code must fail cleanly with a clear message when not elevated — never half-apply.
