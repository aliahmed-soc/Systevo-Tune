using SystevoTune.ConsoleRunner;
using SystevoTune.Engine;

// Dev-only harness for the Engine. Not shipped to users — the real front end is the WPF app
// (Phase 3). Everything it prints comes from engine results; it holds no logic of its own.
//
// Commands that change the machine refuse to run without --vm. That flag is a deliberate
// speed bump: this tool is only ever meant to apply changes inside a throwaway VM, and a
// mistyped command on a real desktop should do nothing at all.

return await Commands.RunAsync(args).ConfigureAwait(false);
