# Blocked

Things that could not be finished, what was tried, and what you need to decide.

## B1 — Boot time metric (task 12)

**Wanted:** boot time as a before/after number, alongside idle RAM and startup app count.

**Status:** not built. The other two metrics are done and tested.

**What I tried:**

- `Environment.TickCount64` gives uptime, not how long the boot took. Reporting it as "boot
  time" would be dishonest, and doc 01 rules out overselling numbers.
- Windows records real boot duration in the event log:
  `Microsoft-Windows-Diagnostics-Performance/Operational`, event ID 100, field `BootTimeMs`.
  Reading it needs the `System.Diagnostics.EventLog` NuGet package — it is not in the
  `net8.0-windows` framework.
- `wmic`/CIM alternatives would need `System.Management`, also a package.

**Why I stopped:** rule 8 says a new package needs justification, and this is a
"only if time remains" metric. Adding a dependency for a nice-to-have without you here felt
like the wrong call, especially as it is the first package in the project beyond the xUnit
test template.

**Your options:**

1. Approve `System.Diagnostics.EventLog` and I read event 100. Real number, one dependency.
   Note it only exists after at least one boot since the log was enabled, and it is empty on a
   freshly imaged VM until the second boot.
2. Drop the metric. Freed space and startup app count are the honest, reliable ones.
3. Show uptime instead, labelled "time since last restart" — no package, no dependency, but a
   different thing from boot time.

My recommendation is **2 or 3**. Boot time varies enormously between boots on the same machine,
so it makes a poor before/after claim even when measured properly.

## B3 — How should the Windows Update cache be cleaned? (needs your call)

**Found during verification on 2026-07-27.** The path is right; our method is not.

Microsoft's [documented reset](https://learn.microsoft.com/en-us/troubleshoot/windows-client/installing-updates-features-roles/additional-resources-for-windows-update)
stops `bits`, `wuauserv`, and `cryptsvc` before touching `SoftwareDistribution`, and **renames**
`Download` rather than deleting it. We delete file-by-file with nothing stopped, which is both
ineffective (most of it is locked, so freed size will not match scanned size — doc 7.3 checks
exactly that) and risky (a staged update awaiting restart can have unlocked files that are still
needed; locked-file handling does not save us there).

**Interim action taken:** the group is out of both profiles (decision 23) so nothing does it
blindly. It is still in the whitelist and can be ticked deliberately.

**Your options:**

1. **Stop `wuauserv` + `bits`, delete, restart them.** Matches Microsoft's procedure. Costs us a
   service-control dependency and a new undo path (restart the services), and touches a service —
   worth checking against golden rule 4, though Windows Update is not on the forbidden list.
2. **Refuse to clean when an update is pending a restart, otherwise clean as now.** Safer than
   today with no service control, but still leaves most of the folder locked.
3. **Drop the group.** Temp files and the Recycle Bin are the honest wins; the update cache
   refills itself anyway.

My recommendation is **1 if you want the space, 3 if you want the simplicity.** Option 2 gets the
risk down but keeps the "size shown ≠ size freed" problem, which is the one doc 7.3 will fail on.

## B2 — Verification of every Windows path is still outstanding

Not a blocker for the build — a blocker for running anything. Items sit under **UNVERIFIED** in
`.claude/skills/windows-verified-paths/SKILL.md`. They came from model knowledge, exactly as
rule 3 anticipated, and are flagged rather than presented as fact.

**Progress on 2026-07-27:** the three highest-risk items were checked against Microsoft docs.

- **U22 (GameDVR policy) — verified and moved to the verified table.** Correct as written.
- **U8 (update cache) — path right, method wrong.** Became B3 above.
- **U27 (startup approvals) — assumption wrong, corrected in code.** Microsoft does not document
  `StartupApproved` at all, so it cannot be settled from docs; it needs an empirical check in the
  VM. The correction came from DFIR and Sysinternals-community sources.

The rest (U1–U7, U9–U21, U23–U26, U28–U31) are untouched and still need checking.
