using SystevoTune.Engine;

namespace SystevoTune.Engine.Tests;

/// <summary>
/// Smoke test proving the test project is wired to the Engine.
/// Replaced by real coverage as modules land.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void Engine_is_referenced_and_identifies_itself()
    {
        Assert.Equal("SystevoTune", EngineInfo.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(EngineInfo.Version));
    }
}
