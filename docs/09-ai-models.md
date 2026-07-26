# 9. AI Models — Which One for What

Two different questions here. Keep them apart.

1. Which AI model helps you **build** the app.
2. Does the app itself need AI **inside** it.

## 9.1 Building the app (your dev workflow)

You already use Claude Code, so stay in that flow. Split the work by model:

| Job | Model | Why |
|---|---|---|
| Architecture, safety-layer design, hard debugging | Claude Opus 4.8 (or Fable 5 if your plan has it) | Deepest thinking. Use it where a wrong choice is costly. |
| Daily coding: C# modules, tests, WPF screens | Claude Sonnet 4.6 | Best balance of quality, speed, and cost. Your main driver. |
| Small edits, boilerplate, renames, quick fixes | Claude Haiku 4.5 | Fast and cheap. Don't burn big-model time on small jobs. |

Simple rule: **think with the big model, build with the middle one, patch with the small one.**

Model access depends on your plan. Check the current list here: https://docs.claude.com/en/docs/about-claude/models

### One honest warning

C# and .NET are well covered by all top models. The risky part is **Windows internals**: registry paths, service names, power plan GUIDs. Any AI model can invent a registry key that looks real but is not. So:

- Verify every registry path and service name against Microsoft docs.
- Test every tweak in the VM before trusting it.
- This is exactly why the change log stores old values — even a wrong tweak gets undone.

The model matters less than the process. A good plan + VM tests + undo beats any model choice.

## 9.2 AI inside the app?

**Decision: no AI inside version 1.** Reasons:

- The app's promise is safety through **fixed whitelists**. An AI deciding system changes = guessing. That breaks rule 5.4 ("no smart guessing").
- The app must work **offline**. API models need internet and cost money per call. The app is free, so every call is a loss with no income.
- No AI = **deterministic** (same input, same output, every time). That is what makes the test plan in file 07 possible. AI output changes run to run — you cannot test it the same way.

### Safe AI ideas for later (v2+)

| Idea | How | Risk |
|---|---|---|
| "Explain this tweak" text | Pre-write the text once (with AI help, offline, during development). Ship it as static strings in English + Arabic. | None. No API in the app. |
| Support chat | On the Systevo website, not inside the app. | Low. |
| Smart suggestions in-app | If ever: Claude Haiku via API, and it may only **pick from the whitelist**, never invent changes. | Medium. Only with clear user opt-in. |

### The rule

**AI can explain. AI never executes.** The engine stays deterministic forever.
