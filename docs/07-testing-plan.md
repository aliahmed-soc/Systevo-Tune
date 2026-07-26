# 7. Testing Plan

Goal: **95% bug-free, 100% recoverable.** Any bug that Undo cannot fix is a release blocker.

## 7.1 Test machines (VMs)

| VM | Why |
|----|-----|
| Windows 11 latest, clean install | The main target |
| Windows 10 22H2, clean install | Still a huge user base |
| Windows 11 "dirty" (many apps, old drivers) | The real-world mess |
| Windows in Arabic | RTL layout + localized service names |
| A real laptop (or laptop-profile VM) | Battery rules |

Take a snapshot before every test run. Rewind after.

## 7.2 The key test (run for every module)

1. Snapshot the VM.
2. Run apply with everything turned on.
3. Run Undo All.
4. Compare system state to the snapshot: power plan, touched registry keys, services, startup items.
5. Any difference = a bug.

## 7.3 Module checklists

**Cleanup**
- [ ] Never deletes anything outside whitelist paths
- [ ] Handles locked/in-use files without crashing
- [ ] Size shown ≈ size actually freed

**Startup**
- [ ] Disable + re-enable works for both registry items and startup-folder items
- [ ] No item is ever deleted, only disabled

**Services**
- [ ] Only whitelist services are changed
- [ ] Undo restores the exact previous start type (not a default)

**Power / visuals / gaming toggles**
- [ ] Correct on both Windows 10 and 11
- [ ] Settings survive a reboot
- [ ] Undo restores the previous values, not Windows defaults

## 7.4 Nasty cases

- [ ] Run without admin rights → clean message, zero half-changes
- [ ] Kill the app mid-apply → log shows the partial run, Undo still works
- [ ] Run apply twice in a row → no double-log mess, undo still correct
- [ ] Restore points disabled on the PC → warning shown
- [ ] Disk almost full → cleanup still behaves safely
- [ ] Non-English Windows → no crashes on service names or paths

## 7.5 Unit tests (engine)

- xUnit tests for pure logic: log read/write, undo ordering, whitelist checks, profile parsing.
- System-touching parts get tested through the VM plan above, not unit tests.

## 7.6 Beta

- ~10 users from the IT circle, mixed PCs.
- Ask each to run: Gaming → Undo → Work → Undo. Report anything odd.
- Collect logs (with consent). Fix. Then ship v1.0.

## 7.7 Bug severity rules

| Level | Meaning | Action |
|---|---|---|
| Blocker | Undo cannot fix it, or the PC is harmed | Stop the release |
| Major | Feature wrong, but recoverable | Fix before release |
| Minor | UI or text issues | Fix when possible |
