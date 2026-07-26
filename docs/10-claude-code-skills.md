# 10. Claude Code Skills for This Project

Goal: make every Claude Code session follow the project rules without you repeating them.

## 10.1 Quick recap

- A skill = a folder with a `SKILL.md` file (name + description + instructions).
- Put project skills in `.claude/skills/` **inside the repo**. They travel with git, and any machine (or teammate) gets them.
- Skills load only when needed, so they cost almost nothing until triggered.
- The `description` line is the trigger. Vague description = skill never fires. Be exact.

Docs: https://code.claude.com/docs/en/skills

## 10.2 CLAUDE.md vs skills (split the rules)

| Put in project `CLAUDE.md` (always loaded) | Put in skills (loaded on demand) |
|---|---|
| The golden rules (short) | Long procedures and checklists |
| Project structure map | Verified paths reference data |
| Build/test commands | Test run steps, release steps |

Paste-ready block for the project `CLAUDE.md`:

```markdown
# Golden rules (never break these)
1. Log first, change second. Every system change writes a JSON log entry BEFORE it runs.
2. Whitelists only. Never touch a service, app, or path not in the whitelist files.
3. Every change must have a working undo path. No undo = do not build it.
4. Never touch: Defender, firewall, network/audio/printer services, user files.
5. Never invent registry paths. Only use paths from the verified-paths skill.
6. Engine has zero UI code. UI only calls the Engine.
```

## 10.3 Custom skills to create (in priority order)

**1. `windows-tweak-safety` — build this first.** Full example below. Fires on any code that changes Windows settings. This is the safety layer (file 05) turned into enforced instructions.

**2. `windows-verified-paths` — the anti-hallucination skill.** A reference skill holding only checked data: registry paths, service names, power plan GUIDs — each with a Microsoft docs link. Rule inside: "If a path is not in this file, stop and ask the user to verify it first." Grows as the project grows.

**3. `engine-conventions`** — C# rules: project layout (Engine / App / Cli), xUnit test pattern, the change-log record shape (from file 05), naming, error handling style.

**4. `vm-undo-test`** — the key test as a step-by-step procedure: snapshot → apply all → undo all → compare state → report differences. Fires when you say "run the undo test" or after finishing any module.

**5. `wpf-ui-conventions`** — dark theme tokens, all text via resource files (no hard-coded strings), English + Arabic keys, RTL layout rules.

**6. `release-checklist`** — Phase 5 steps: version bump, build installer + portable exe, hashes, GitHub release notes, Systevo page update.

## 10.4 Full example: the safety skill

Save as `.claude/skills/windows-tweak-safety/SKILL.md`:

```markdown
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
```

## 10.5 Ready-made skills worth using

- **skill-creator** (Anthropic's example skills) — use it once to generate and refine the six skills above.
- Engineering-type skills you may already have (code review, testing strategy, debug): fine to keep, they fire on general coding work.
- Document skills (Word/Excel/PDF) are not needed for this project.
- With 76+ skills installed: watch that unrelated skills don't fire on this repo. Sharp descriptions on the six project skills keep routing clean.

## 10.6 When to build them

- Before Phase 1: `windows-tweak-safety`, `windows-verified-paths`, `engine-conventions`, project `CLAUDE.md`.
- With Phase 1: `vm-undo-test`.
- Before Phase 3: `wpf-ui-conventions`.
- Before Phase 5: `release-checklist`.

One hour of skill setup saves you from repeating the safety rules in every prompt — and from the one hallucinated registry path that could hurt a tester's PC.
