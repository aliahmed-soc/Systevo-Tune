namespace SystevoTune.Engine;

/// <summary>
/// Identity of the engine. Kept UI-free on purpose: the Engine never prints,
/// never prompts, and never knows who is calling it.
/// </summary>
public static class EngineInfo
{
    /// <summary>Product name used for the ProgramData folder and log paths.</summary>
    public const string ProductName = "SystevoTune";

    /// <summary>Engine version. Bumped per release, not per commit.</summary>
    public const string Version = "0.1.0";
}
