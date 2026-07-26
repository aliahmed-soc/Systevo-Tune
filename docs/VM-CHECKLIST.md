# VM Checklist

Everything the engine assumes about Windows that has not been proven on a real machine, and the
exact command that proves it.

Work top to bottom. Each check is read-only unless it says otherwise — run them **before** the
first `apply`, on a clean VM snapshot.

**How to record a result:** when a check passes, move its row in
`.claude/skills/windows-verified-paths/SKILL.md` from Tier 2 to Tier 1 with today's date. When it
fails, that is a bug — write it in `docs/BLOCKED.md` before changing any code.

---

## 0. Before anything

```powershell
[System.Environment]::OSVersion.Version; (Get-ComputerInfo).WindowsProductName
```

Record the build and edition. Several assumptions are edition-sensitive: **`AllowGameDVR` and
`AllowTelemetry` both list Pro/Enterprise/Education/IoT and not Home.** If this VM is Home,
expect those two to do nothing, and that is a finding rather than a bug.

Then take the VM snapshot. Nothing below step 2 is safe without it.

---

## 1. Read-only checks

### Power schemes — O1, N1, V1–V3

```powershell
powercfg /list
```

- [ ] **O1.** Do the listed GUIDs match `381b4222…` (Balanced), `8c5e7fda…` (High)? On an OEM
      image they may not. If they differ, the runtime name-matching should still find them —
      confirm with `preview gaming` in step 2.
- [ ] **N1.** Is Ultimate Performance (`e9a42b02…`) listed? Usually absent on consumer installs.
- [ ] **O2.** Does `powercfg /getactivescheme` return the same scheme the `*` marks in `/list`?

### GPU scheduling — N7, O4

```powershell
Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name HwSchMode -ErrorAction SilentlyContinue
```

- [ ] **N7.** Present? Value `2` = on, `1` = off.
- [ ] **O4.** If **absent** on a machine whose Settings app *does* offer "Hardware-accelerated GPU
      scheduling", that confirms absent means "Windows is deciding" rather than "unsupported" —
      and confirms our `NotApplicable` message is the honest one.

### Visual effects — N2, N3, N4

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name VisualFXSetting -ErrorAction SilentlyContinue
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name EnableTransparency -ErrorAction SilentlyContinue
Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name MinAnimate -ErrorAction SilentlyContinue
```

- [ ] **N2.** `VisualFXSetting` exists and is 1/2/3.
- [ ] **N3.** `EnableTransparency` exists and is 0/1.
- [ ] **N4.** `MinAnimate` is a **string** (`"0"`/`"1"`), not a DWORD. If it is a DWORD here, the
      whitelist type is wrong and undo would restore the wrong kind.

### Game Mode and Game Bar — N5, N6, N16, N17

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -ErrorAction SilentlyContinue
Get-ItemProperty 'HKCU:\System\GameConfigStore' -Name GameDVR_Enabled -ErrorAction SilentlyContinue
Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR' -ErrorAction SilentlyContinue
```

- [ ] **N5.** `AutoGameModeEnabled` present.
- [ ] **N6.** `GameDVR_Enabled` present.
- [ ] **N16.** `HistoricalCaptureEnabled` present. Toggle **Settings → Gaming → Captures → Record
      what happened** and re-read: this value should follow it. That is the check that proves
      N16 is the background-recording lever.
- [ ] **N17.** `AppCaptureEnabled` present, and follows the master capture toggle.

### Privacy — N18–N23

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' | Format-List
```

- [ ] **N18–N23.** All six named values exist.
- [ ] The `SubscribedContent-NNNNNN` numbers are **the least trustworthy entries in the whole
      whitelist** — opaque Microsoft content ids that can change between builds. Toggle
      **Settings → Personalisation → Start → Show recommendations…** and **Settings → System →
      Notifications → Get tips…**, re-read, and confirm which id moved. Correct the whitelist if
      an id turns out to mean something else.
- [ ] `RotatingLockScreenEnabled` (the Spotlight **wallpaper**) is untouched by us — confirm the
      wallpaper still rotates after an apply.

### Startup — N10–N13, and the O-question that replaced U27

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32' -ErrorAction SilentlyContinue
```

- [ ] **N11.** Values are REG_BINARY, 12 bytes.
- [ ] Flag byte: disable something in Task Manager → Startup and re-read. First byte should become
      `0x03`; an enabled item should read `0x02` or `0x06`.
- [ ] Bytes 4–11 should change when you disable (a FILETIME of the disable time).
- [ ] **N12 — the important one.** Put a shortcut in
      `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup`, disable it in Task Manager,
      then check **which hive** gained the value:

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder' -ErrorAction SilentlyContinue
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder' -ErrorAction SilentlyContinue
```

  The whitelist pairs the all-users folder with **HKLM**. If it is actually HKCU, disabling an
  all-users startup item will silently do nothing — the exact bug this correction was meant to fix.
- [ ] Confirm the value name includes the `.lnk` extension.
- [ ] **N13.** Does `StartupApproved\Run32` exist on this 64-bit VM?

### System Restore — N8, N9

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -ErrorAction SilentlyContinue
Get-ComputerRestorePoint | Measure-Object
```

- [ ] **N8.** `RPSessionInterval` present, and `0` when you turn System Restore off in System
      Protection.
- [ ] **N9.** `DisableSR` appears under the policy key when you disable via Group Policy.
- [ ] **O3/O5.** `Get-ComputerRestorePoint` returns a count without error. This is now the
      authority for the restore-point outcome, so it must work.

### Cleanup paths — N14, N15, and U8's successor

```powershell
"$env:TEMP", "$env:WINDIR\Temp", "$env:SystemDrive\`$Recycle.Bin" | ForEach-Object { Test-Path $_ }
Get-ChildItem "$env:WINDIR\SoftwareDistribution\Download" | Measure-Object -Property Length -Sum
```

- [ ] **N14, N15.** All three exist.
- [ ] Note the update cache size — step 3 checks the freed size against it.

### Services — V12

```powershell
sc.exe query wuauserv
Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\wuauserv' -Name Start
```

- [ ] **V12.** `Start` is a DWORD in 0–4.
- [ ] `sc query` prints `STATE : <number>`. Confirm the number is there even on a localised
      Windows — the parser reads only the number.

### Bloatware — N24

```powershell
Get-AppxPackage | Select-Object Name | Sort-Object Name
```

- [ ] **N24.** Check each of the eight names in `Whitelists/bloatware.json` against this list.
      Mark `approved: true` only for ones you have confirmed **and** want gone. A name that no
      longer exists is harmless; the approval gate is what keeps a wrong one harmless too.

---

## 2. The dry run — still safe

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- scan
```

- [ ] Sizes look plausible, and no group reports `REJECTED BY GUARD`.

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- preview gaming
```

- [ ] Every tweak shows `Ready`, `AlreadyApplied` or `NotApplicable` — no `Blocked`.
- [ ] Old values match what you read in step 1. **This is the real O1 check:** if the power plan
      line names your actual current plan, runtime matching worked on this machine.
- [ ] Nothing changed — re-run a couple of step-1 reads and confirm.

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- startup
```

- [ ] The list matches Task Manager → Startup, including which items are off.

---

## 3. The doc 07.2 cycle — changes the machine

Snapshot the VM first. Then, from an elevated prompt:

```bash
dotnet run --project src/SystevoTune.ConsoleRunner -- verify gaming --vm
```

This does the whole thing: snapshot → apply → snapshot → Undo All → snapshot → diff.

- [ ] **Exit code 0 and `PASS`.** Anything else is a bug — doc 07.2 says any difference is one.
- [ ] `INCONCLUSIVE` means the profile changed nothing, so nothing was proved. Roll back to a
      clean snapshot and try again before applying anything else.
- [ ] Read `report.md` under `C:\ProgramData\SystevoTune\verify\<run>-<profile>\`. The
      "What the profile changed" table should list things you recognise from step 1.
- [ ] Deleted temp files appear under "Permanent by design". That is correct, not a failure.
- [ ] Repeat for `work`.

Then the manual half of doc 07.2, which the harness cannot do:

- [ ] Reboot. Do the settings survive? (Doc 7.3: "Settings survive a reboot".)
- [ ] After a reboot following an `apply`, does Windows Update still work? That is the
      `wuauserv`/`bits` restart from decision H1.

## 4. Doc 07.4's nasty cases

- [ ] **No admin rights.** Run `apply gaming --vm` from a normal prompt → clean refusal, zero
      changes.
- [ ] **No VM flag.** Run `apply gaming` → refused before anything starts.
- [ ] **Kill mid-apply.** Ctrl-C during an apply, then `runs` → the partial run is listed, and
      `undo --vm` still works.
- [ ] **Apply twice.** `apply gaming --vm` twice, then `undo --vm` once → back to the original
      values, not to the state between the two runs.
- [ ] **Restore points disabled.** Turn System Restore off, run `apply` → a warning, not a crash.
- [ ] **Update cache with the service busy.** Start a Windows Update download, then clean the
      update cache group → it should skip with a warning and leave both services running.
- [ ] **Non-English Windows.** Repeat step 2 and step 3 on an Arabic VM. This is the one that
      exercises every "read the number, ignore the word" decision in the codebase.

## 5. Re-apply

- [ ] `apply gaming --vm`, then change one tweaked value by hand, then
      `reapply --vm` → only that one value is rewritten.

---

## Not covered by any of this

- **B1, boot time.** Not built; needs your decision on the `System.Diagnostics.EventLog` package.
- **Services whitelist.** Ships empty by design. Nothing to check until you fill it.
- **Bloatware removal.** Ships with nothing approved, so `verify` never exercises it. Approve one
  entry and re-run `verify` if you want that path proven — and expect the undo to fail, because
  reinstalling needs the Store.
