using SystevoTune.Engine.Platform.Windows;

namespace SystevoTune.Engine.Tests.Platform;

/// <summary>
/// Parsing <c>powercfg /list</c>. Doc 07.4 requires non-English Windows to work, so these
/// pin down that the parser never depends on the labels.
/// </summary>
public class PowerCfgParsingTests
{
    private const string EnglishOutput = """

        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)

        """;

    [Fact]
    public void Every_scheme_is_found()
        => Assert.Equal(3, PowerCfgPowerPlanService.Parse(EnglishOutput).Count);

    [Fact]
    public void The_active_scheme_is_the_one_with_the_star()
    {
        var active = PowerCfgPowerPlanService.Parse(EnglishOutput).Single(plan => plan.IsActive);

        Assert.Equal(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), active.Id);
    }

    [Fact]
    public void Names_are_kept_for_display()
        => Assert.Equal("High performance", PowerCfgPowerPlanService.Parse(EnglishOutput)[1].Name);

    [Fact]
    public void An_arabic_windows_still_parses_because_only_the_guid_and_star_are_read()
    {
        const string arabic = """
            أنظمة الطاقة الموجودة (* نشط)
            -----------------------------------
            معرف نظام الطاقة: 381b4222-f694-41f0-9685-ff5bb260df2e  (متوازن)
            معرف نظام الطاقة: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (أداء عالي) *
            """;

        var plans = PowerCfgPowerPlanService.Parse(arabic);

        Assert.Equal(2, plans.Count);
        Assert.Equal(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), plans.Single(plan => plan.IsActive).Id);
    }

    [Fact]
    public void Headers_and_separators_are_ignored()
        => Assert.Empty(PowerCfgPowerPlanService.Parse("Existing Power Schemes (* Active)\r\n------------\r\n"));

    [Fact]
    public void Empty_output_yields_no_plans()
        => Assert.Empty(PowerCfgPowerPlanService.Parse(string.Empty));

    [Fact]
    public void A_scheme_with_no_name_in_brackets_still_parses()
    {
        var plans = PowerCfgPowerPlanService.Parse("GUID: 381b4222-f694-41f0-9685-ff5bb260df2e *");

        Assert.True(Assert.Single(plans).IsActive);
    }
}
