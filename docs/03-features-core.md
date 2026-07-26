# 3. Core Features (Version 1)

Each feature says what Gaming and Work modes do with it.

## 3.1 Cleanup

Delete safe junk only: temp files, Windows Update cache, browser cache, Recycle Bin.

- Scan first. Show size found per group. User ticks what to clean.
- Never touch user files (Documents, Desktop, Downloads).
- Same in both modes.

## 3.2 Startup control

- List apps that start with Windows, with their delay impact (Windows already tracks this).
- **Disable, never delete.** One click to re-enable.
- Gaming: suggest cutting everything not needed. Work: suggest only the heavy ones.

## 3.3 Services tuning

A service is a background program Windows runs all the time.

- Keep a **whitelist**: services known safe to set to "Manual" (start only when needed).
- Never touch security, network, audio, or printer services.
- Every change logged for undo.

## 3.4 Power plan

- Gaming: High Performance (or Ultimate Performance if present).
- Work: Balanced.
- Laptop on battery: warn before switching to High Performance.

## 3.5 Visual effects

- Gaming: turn off animations and transparency.
- Work: keep them on.

## 3.6 Gaming extras

- Turn on Windows **Game Mode**.
- Turn on **Hardware-accelerated GPU scheduling** (the GPU manages its own memory; small latency win). Needs a restart — tell the user.
- Turn off **Xbox Game Bar** background recording.

## 3.7 Network (optional)

- Offer a DNS switch: Cloudflare 1.1.1.1 or AdGuard DNS.
- **Off by default.** User choice only. One click to revert.

## 3.8 Bloatware remover

Bloatware = preloaded junk apps (games, trials, shopping apps).

- Whitelist of known junk. Checkbox list. Nothing removed without a tick.
- Removal via PowerShell (`Remove-AppxPackage`).

## 3.9 Privacy

- Turn off telemetry where allowed (telemetry = Windows sending usage data to Microsoft).
- Turn off ads and "tips" in Start menu and lock screen.

## Profile summary

| Feature | Gaming | Work |
|---|---|---|
| Power plan | High / Ultimate | Balanced |
| Visual effects | Off | On |
| Game Mode | On | Off |
| Startup apps | Cut hard | Cut heavy only |
| Cleanup | Yes | Yes |
| Privacy tweaks | Yes | Yes |
| DNS switch | Optional | Optional |
