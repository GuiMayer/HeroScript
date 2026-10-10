using Core.Common;
using Core.Run;

namespace Core.Meta;

/// <summary>Conservative monotonic origin: Published &lt; Experimental &lt; Sandbox &lt; DevModder &lt; Branch.
/// Changing capabilities cannot erase earlier experimental provenance. Simulations never contribute.</summary>
public static class RunProgressProvenance
{
    public static ProfileProgressProvenance Effective(RunState run)
    {
        var mode = run.ResolvedMode;
        var intrinsic = run.Lineage?.ParentRunId != null ? ProfileProgressProvenance.Branch :
            mode?.Definition.ProfileProgressProvenance is ProfileProgressProvenance.DevModder or ProfileProgressProvenance.Sandbox
                ? mode.Definition.ProfileProgressProvenance.Value :
            run.Scenario != null ? ProfileProgressProvenance.Sandbox :
            mode?.CapabilityPolicy is { } capability && (capability.AllowRunResourceCheats || capability.AllowCardZoneCheats)
                ? ProfileProgressProvenance.Experimental : mode?.Definition.ProfileProgressProvenance ?? ProfileProgressProvenance.Published;
        return Combine(run.ProgressProvenance ?? ProfileProgressProvenance.Published, intrinsic);
    }

    public static ProfileProgressProvenance Combine(ProfileProgressProvenance previous, ProfileProgressProvenance next) =>
        (ProfileProgressProvenance)System.Math.Max((int)previous, (int)next);

    public static Result Validate(RunState? before, RunState after)
    {
        if (after.ProgressProvenance is { } recorded && !Enum.IsDefined(recorded)) return Result.Failure("Invalid progress provenance");
        return before != null && (int)Effective(after) < (int)Effective(before)
            ? Result.Failure("Run progress provenance cannot be downgraded") : Result.Success();
    }
}
