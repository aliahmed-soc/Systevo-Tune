# 2. Tech Stack

## Language

**C# with .NET 8.**

- Best access to Windows internals: registry, services, power plans, WMI.
- WMI = Windows Management Instrumentation. A built-in way to read and control Windows parts from code.
- C# feels close to TypeScript, so the jump from your current stack is small.

## UI

| Option | Good | Bad |
|--------|------|-----|
| WPF | Stable, mature, huge docs and answers online | Looks older out of the box |
| WinUI 3 | Modern Windows 11 look | Younger, more rough edges |

**Decision for v1: WPF** + a modern theme library (ModernWpf or "WPF UI"). Revisit WinUI later if needed.

## Project structure

Three parts, kept apart:

```
Solution
├── Optimizer.Engine   → C# class library. Does the real work. No UI code.
├── Optimizer.App      → WPF window app. Only talks to the Engine.
└── Optimizer.Cli      → (later) command-line front for the same Engine.
```

Why apart: the engine can be tested alone, and reused by both the app and the CLI.

## Other choices

- Some tasks run through **PowerShell called from C#** (e.g. removing store apps). Faster to write.
- The app must **run as admin**. It asks for admin rights at start (the UAC prompt).
- Change logs saved as **JSON** files in `C:\ProgramData\<AppName>\logs`.
- Settings saved as JSON too. No cloud, no account.
- Ship both an **installer** and a **portable single .exe**.
- Unit tests with **xUnit** for the engine (pure logic parts).
