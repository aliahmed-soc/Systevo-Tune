using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Profiles;

/// <summary>
/// Turns a profile into the ordered list of tweaks the runner executes. Applying a profile is
/// therefore exactly the same code path as applying tweaks one by one: same preview, same log,
/// same undo.
/// </summary>
public sealed class ProfileBuilder(
    CleanupModule cleanup,
    RegistryTweakCatalog registryTweaks,
    IRegistryService registry,
    IPowerPlanService powerPlans,
    PowerPlanCatalog powerPlanCatalog,
    IBatteryStatus battery)
{
    /// <summary>
    /// Builds the tweaks for a profile, in file order.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A step names an id that is not in the matching whitelist. Thrown at build time rather than
    /// mid-apply, so a bad profile can never half-run.
    /// </exception>
    public IReadOnlyList<ITweak> Build(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var tweaks = new List<ITweak>();

        foreach (var step in profile.Steps)
        {
            tweaks.Add(step.Kind switch
            {
                ProfileStepKind.Cleanup => BuildCleanup(profile, step),
                ProfileStepKind.Registry => BuildRegistry(profile, step),
                ProfileStepKind.PowerPlan => BuildPowerPlan(profile, step),
                _ => throw new InvalidOperationException(
                    $"Profile '{profile.Id}' has a step of an unknown kind ({step.Kind})."),
            });
        }

        return tweaks;
    }

    private ITweak BuildCleanup(Profile profile, ProfileStep step)
    {
        var id = Require(profile, step, step.Id);

        return cleanup.CreateTweaks([id]).SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Profile '{profile.Id}' names cleanup group '{id}', which is not in the cleanup whitelist.");
    }

    private ITweak BuildRegistry(Profile profile, ProfileStep step)
    {
        var id = Require(profile, step, step.Id);
        var entry = registryTweaks.Find(id)
            ?? throw new InvalidOperationException(
                $"Profile '{profile.Id}' names registry tweak '{id}', which is not in the registry whitelist.");

        return new RegistryTweak(registry, entry);
    }

    private ITweak BuildPowerPlan(Profile profile, ProfileStep step)
    {
        var preferred = step.Preferred is { Count: > 0 }
            ? step.Preferred
            : throw new InvalidOperationException($"Profile '{profile.Id}' has a power plan step with no plans.");

        foreach (var id in preferred)
        {
            if (powerPlanCatalog.Find(id) is null)
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.Id}' names power plan '{id}', which is not in the power plan whitelist.");
            }
        }

        return new PowerPlanTweak(powerPlans, powerPlanCatalog, battery, preferred);
    }

    private static string Require(Profile profile, ProfileStep step, string? id)
        => !string.IsNullOrWhiteSpace(id)
            ? id
            : throw new InvalidOperationException($"Profile '{profile.Id}' has a {step.Kind} step with no id.");
}
