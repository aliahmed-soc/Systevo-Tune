# Systevo Tune

A Windows PC tune-up tool. C# / .NET 8. Gaming and Work profiles, English + Arabic.
Full plan lives in [docs/](docs/) — `docs/README.md` is the index.

The promise this whole product rests on: **no change the app makes can be permanent.**

# Golden rules (never break these)
1. Log first, change second. Every system change writes a JSON log entry BEFORE it runs.
2. Whitelists only. Never touch a service, app, or path not in the whitelist files.
3. Every change must have a working undo path. No undo = do not build it.
4. Never touch: Defender, firewall, network/audio/printer services, user files.
5. Never invent registry paths. Only use paths from the verified-paths skill.
6. Engine has zero UI code. UI only calls the Engine.

## Project structure

```
SystevoTune.sln
├── src/SystevoTune.Engine          class library — all real logic, zero UI code
│   └── Whitelists/                 cleanup paths, services, bloatware (data files, not code)
├── src/SystevoTune.ConsoleRunner   console app — dev testing only, never shipped
├── tests/SystevoTune.Engine.Tests  xUnit — pure logic only
└── docs/                           the plan (01..10)
```

Later phases add `SystevoTune.App` (WPF) and `SystevoTune.Cli`. Both call the Engine, neither
duplicates its logic.

Change logs are written to `C:\ProgramData\SystevoTune\logs`, one JSON file per run.

## Build and test

```
dotnet build SystevoTune.sln
dotnet test  SystevoTune.sln
dotnet run --project src/SystevoTune.ConsoleRunner
```

Targets `net8.0-windows`. A .NET 9 SDK builds it fine.

## Working agreements

- **Never run apply or undo on this machine.** Build and unit tests only. Every run that touches
  real system state happens in the user's VM, started by the user.
- Ask before adding any NuGet package.
- Small commits, one step each. A tweak and its undo ship in the same commit.
- Registry paths, service names, and GUIDs come from the `windows-verified-paths` skill.
  Not in that file = stop and ask. Never guess.

## Project skills

`.claude/skills/` — `windows-tweak-safety`, `windows-verified-paths`, `engine-conventions`.
They travel with the repo. Read them before touching Engine code.
