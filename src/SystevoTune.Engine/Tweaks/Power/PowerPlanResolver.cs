using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Tweaks.Power;

/// <summary>
/// Finds a wanted plan among the schemes a PC actually has.
/// </summary>
/// <remarks>
/// This exists because of open question O1. Microsoft documents the three scheme GUIDs as
/// <c>GUID_POWERSCHEME_PERSONALITY</c> values, and says every scheme "maps to" one of them — so a
/// stock install uses those ids directly, but an OEM image may ship its own scheme that merely
/// maps to a personality. Assuming the GUID would make the tweak silently do nothing there.
/// <para>
/// Pure and side-effect free, so every awkward machine shape is a unit test rather than a VM run.
/// </para>
/// </remarks>
internal static class PowerPlanResolver
{
    /// <summary>
    /// The scheme on this PC matching <paramref name="wanted"/>, or <c>null</c>.
    /// GUID first because it is exact; name second because it is the only other signal we have.
    /// </summary>
    public static PowerPlan? Match(IReadOnlyList<PowerPlan> available, PowerPlanEntry wanted)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(wanted);

        var byId = available.FirstOrDefault(plan => plan.Id == wanted.Guid);
        if (byId is not null)
        {
            return byId;
        }

        // A scheme this engine created earlier counts as the plan too, otherwise a second run
        // would create a duplicate.
        if (wanted.CreateAs is { } createdId)
        {
            var byCreatedId = available.FirstOrDefault(plan => plan.Id == createdId);
            if (byCreatedId is not null)
            {
                return byCreatedId;
            }
        }

        foreach (var name in wanted.AllNames())
        {
            var byName = available.FirstOrDefault(
                plan => string.Equals(plan.Name, name, StringComparison.OrdinalIgnoreCase));

            if (byName is not null)
            {
                return byName;
            }
        }

        return null;
    }

    /// <summary>
    /// The first wanted plan this PC has, walking the preference list in order.
    /// </summary>
    public static (PowerPlanEntry Entry, PowerPlan Plan)? MatchFirst(
        IReadOnlyList<PowerPlan> available,
        IReadOnlyList<PowerPlanEntry> wanted)
    {
        foreach (var entry in wanted)
        {
            if (Match(available, entry) is { } plan)
            {
                return (entry, plan);
            }
        }

        return null;
    }

    /// <summary>
    /// Names what the PC does have, for the message shown when nothing matched. An unhelpful
    /// "not available" tells the user nothing; the list tells them what they are working with.
    /// </summary>
    public static string DescribeAvailable(IReadOnlyList<PowerPlan> available)
        => available.Count == 0
            ? "this PC reports no power schemes at all"
            : "this PC offers " + string.Join(", ", available.Select(plan =>
                string.IsNullOrWhiteSpace(plan.Name) ? plan.Id.ToString("D") : plan.Name));
}
