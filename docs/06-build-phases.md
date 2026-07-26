# 6. Build Phases

Rough total: 9–12 weeks part-time. Each phase has a clear "done when."

## Phase 1 — Engine + safety (2–3 weeks)

- [ ] Solution setup: `Optimizer.Engine` library + a console runner for testing
- [ ] Restore point creation
- [ ] Change log (JSON write/read, one file per run)
- [ ] Undo engine (Undo All + per-item)
- [ ] Cleanup module (temp, update cache, recycle bin) with scan-first
- [ ] Dry-run mode
- [ ] All testing inside a VM (virtual machine — a fake PC inside your PC, safe to break)

**Done when:** apply → Undo All leaves the VM exactly as before. Prove it by comparing with a VM snapshot.

## Phase 2 — Profiles (2 weeks)

- [ ] Power plan switch
- [ ] Visual effects switch
- [ ] Game Mode + Game Bar + GPU scheduling toggles
- [ ] Startup manager (list, delay impact, disable/enable)
- [ ] Gaming and Work presets stored as JSON profile files

**Done when:** both presets apply and undo cleanly on Windows 10 and Windows 11 VMs.

## Phase 3 — UI (2–3 weeks)

- [ ] WPF app shell, dark theme
- [ ] Screen 1: scan results
- [ ] Screen 2: review changes (checkboxes, old → new values)
- [ ] Screen 3: apply progress
- [ ] Screen 4: results + big "Undo All" button
- [ ] All English text in one resource file (ready for Arabic later)

**Done when:** a non-tech friend can run a full cycle with no help.

## Phase 4 — Polish (2 weeks)

- [ ] Services tuning (whitelist)
- [ ] Bloatware remover
- [ ] Privacy tweaks
- [ ] Before/after score: boot time, idle RAM, startup app count
- [ ] Arabic UI + RTL layout
- [ ] "Re-apply after Windows update" button

**Done when:** full feature set passes the test plan (file 07) on all VMs.

## Phase 5 — Ship

- [ ] Installer + portable .exe
- [ ] Code signing if budget allows (unsigned system tools trigger antivirus warnings — the biggest headache)
- [ ] Public GitHub repo (open source builds trust for this type of tool)
- [ ] Page on the Systevo website
- [ ] Beta with ~10 users from the IT circle
- [ ] Fix beta findings → release v1.0

**Done when:** beta ran with zero non-recoverable issues.
