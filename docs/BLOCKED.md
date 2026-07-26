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

## B2 — Verification of every Windows path is still outstanding

Not a blocker for the build — a blocker for running anything. 28 items sit under
**UNVERIFIED** in `.claude/skills/windows-verified-paths/SKILL.md` (U1–U28). They came from
model knowledge, exactly as rule 3 anticipated, and are flagged rather than presented as fact.

Nothing has run on any machine, so none of them has been proven. See `SESSION-REPORT.md` for
the ordered list and the three that carry real risk.
