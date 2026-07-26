# 5. Safety Layer (Build This First)

The promise: **no change the app makes can be permanent.** This layer ships before any tweak does.

## 5.1 Restore point

- Create a System Restore Point before every apply run.
- If restore points are turned off on the PC, warn the user and ask before going on.

## 5.2 Change log

Every single change writes one record: what, old value, new value, when.

Example record (JSON):

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

- Logs live in `C:\ProgramData\<AppName>\logs`, one file per run.
- The log is the single source of truth for undo.
- Write the log entry **before** making the change. If the app crashes mid-change, the log still knows.

## 5.3 Undo

- **Undo All**: read the last run's log, put every value back, newest first.
- **Per-item undo**: revert one tweak, keep the rest.
- If one undo step fails, keep going with the others, then show a clear list of what failed.

## 5.4 Whitelists only

- Services, bloatware, and cleanup paths come from fixed lists inside the app.
- Anything unknown is not touched. No "smart" guessing.

## 5.5 Dry run (preview)

- Preview mode shows the full change list: old value → new value. Nothing changes.
- The apply flow is always: preview first, then a second click to apply.

## 5.6 Hard rules

- Never delete user-made files. Only known temp/cache paths.
- Never disable security services (Defender, firewall).
- Never make a change that has no undo path.
- Windows updates may reset tweaks. Add a "Re-apply last profile" button.
