# Services whitelist — draft candidates

**B6. Research only. No code, and nothing here is approved.**

`Whitelists/services.json` ships empty by design. This file is the shortlist to work from — every
row is a *candidate*, and none of it reaches the engine until you move it into that file
yourself.

## How the engine treats a service

It writes only the `Start` value under
`HKLM\SYSTEM\CurrentControlSet\Services\<name>` — verified against
[the Services registry tree](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/hklm-system-currentcontrolset-services-registry-tree).

| Value | Meaning |
|---|---|
| `2` | Automatic — starts with Windows |
| `3` | Manual — starts when something asks for it |
| `4` | Disabled — never starts |

The engine writes `3` or `4` only; `0` and `1` are driver start types and it refuses them. **It
never stops a running service**, so a change takes effect at the next boot and cannot pull the rug
out from under something in use. Undo restores the exact previous value.

## Excluded entirely — doc 03.3 and golden rule 4

Not "not recommended". **Refused at load time**, whatever this file or that one says:

- Security — Defender, its health service, the firewall, the Base Filtering Engine
- Network — DHCP, DNS Client, network location, WLAN, workstation/server, RPC
- Audio — `Audiosrv`, `AudioEndpointBuilder`
- Printing — `Spooler`
- Anything that would break a sign-in — `ProfSvc`, `UserManager`, `gpsvc`, `CryptSvc`, `EventLog`

## Candidates

**All unapproved.** The "why safe" column is the argument to check, not a conclusion.

| Service | What it is | Suggested | Why it may be safe | Docs |
|---|---|---|---|---|
| `SysMain` | Superfetch/prefetch — preloads apps into RAM based on habit | Manual | Designed for spinning disks. On an SSD the benefit is small and the background I/O is real. Widely reported as a CPU/disk hog. **Check the PC actually has an SSD first.** | [SysMain](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config) |
| `DiagTrack` | Connected User Experiences and Telemetry — the diagnostic data uploader | Manual | Pairs with the `AllowTelemetry` policy the privacy tweak already sets. **Overlaps with a tweak we ship** — decide whether both is redundant. | [Diagnostic data](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization) |
| `dmwappushservice` | WAP Push message routing for device management | Manual | Only meaningful under MDM. Irrelevant on a home PC. Often listed alongside DiagTrack. | — |
| `RetailDemo` | Retail Demo Mode, for shop display machines | Disabled | Does nothing outside a shop demo unit. About as safe as this gets. | — |
| `Fax` | Fax service | Manual | Needs a fax modem. Almost nobody has one. | — |
| `WSearch` | Windows Search indexing | Manual | **The most contested one here.** Real disk and CPU savings, but Start menu search and Explorer search get noticeably worse. Arguably a user-facing feature, not bloat — may not belong in a preset at all. | [Windows Search](https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-wds-overview) |
| `MapsBroker` | Downloaded Maps Manager | Manual | Only matters if the Maps app is used offline. | — |
| `PhoneSvc` | Phone Service — telephony state | Manual | For devices with cellular. Idle on a desktop. | — |
| `WalletService` | Wallet | Manual | Legacy; the app is largely gone from Windows 11. | — |
| `RemoteRegistry` | Lets other machines read this registry over the network | Disabled | Already disabled by default on most installs. Reducing remote attack surface fits the Systevo angle. **Confirm it is not needed** if you manage PCs remotely. | [Remote Registry](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config) |

## Three I looked at and left out

Recording these so nobody re-adds them without the argument:

- **`wuauserv`** (Windows Update). Tempting and wrong. Turning updates off is a security decision
  dressed up as performance, and doc 01 says this is not an antivirus but it will not make a PC
  less safe either. The update-cache cleanup already stops it *temporarily* and starts it again.
- **`BITS`**. Windows Update, Store and many installers depend on it. Setting it to Manual is
  mostly harmless because it is demand-started anyway — which is also why there is nothing to gain.
- **`Spooler`**. It is on the forbidden list. Listed here only because it appears on almost every
  "services to disable" list on the internet, and someone will eventually ask why it is missing.

## Before approving any row

1. Confirm the service name with `sc.exe query <name>` on the target Windows build. Names change
   between versions and some of these no longer exist on Windows 11.
2. Add it to `windows-verified-paths` with the Microsoft link.
3. Add it to `Whitelists/services.json` with an English and Arabic name and a one-line reason the
   user reads before ticking it.
4. Re-run `verify gaming --vm` afterwards. Service changes take effect at the next boot, so the
   check is: reboot, confirm the setting held, then Undo All and confirm the original start type
   came back.

## Honest summary

Of the ten candidates, **`RetailDemo`, `Fax` and `dmwappushservice`** are near-certainly safe and
near-certainly worthless — they cost nothing when left alone. **`SysMain`** is the only one with a
measurable payoff, and it depends on the disk. **`WSearch`** is the only one likely to be noticed,
and not in a good way.

Doc 04 puts it well: ten solid features beat forty weak ones. Services tuning may be a feature
that mostly proves it was not worth doing — which is itself worth knowing before shipping it.
