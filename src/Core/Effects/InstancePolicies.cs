using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StackReapplyPolicy { Add, Replace, Highest, Independent }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DurationReapplyPolicy { Preserve, Refresh, Replace, Extend, Maximum }

/// <summary>Shared stack/duration arithmetic, independent of the owning container.</summary>
public static class InstancePolicies
{
    public static Result<int> Stacks(int current, int incoming, int maximum, StackReapplyPolicy policy)
    {
        if (current < 0 || incoming < 1 || maximum < 1 || !Enum.IsDefined(policy))
            return Result<int>.Failure("Invalid stack policy or amount");
        long amount = policy switch
        {
            StackReapplyPolicy.Add => (long)current + incoming,
            StackReapplyPolicy.Highest => System.Math.Max(current, incoming),
            _ => incoming
        };
        return Result<int>.Success((int)System.Math.Min(maximum, amount));
    }

    public static Result<int> Duration(int current, int incoming, int defaultDuration, DurationReapplyPolicy policy)
    {
        if (!ValidDuration(current) || !ValidDuration(incoming) || !ValidDuration(defaultDuration) || !Enum.IsDefined(policy))
            return Result<int>.Failure("Duration must be -1 (permanent) or positive, with a supported reapply policy");
        long result = policy switch
        {
            DurationReapplyPolicy.Preserve => current,
            DurationReapplyPolicy.Refresh => defaultDuration,
            DurationReapplyPolicy.Replace => incoming,
            DurationReapplyPolicy.Maximum => current == -1 || incoming == -1 ? -1 : System.Math.Max(current, incoming),
            DurationReapplyPolicy.Extend => current == -1 || incoming == -1 ? -1 : (long)current + incoming,
            _ => throw new InvalidOperationException()
        };
        return result > int.MaxValue ? Result<int>.Failure("Duration overflow") : Result<int>.Success((int)result);
    }

    public static bool ValidDuration(int duration) => duration == -1 || duration > 0;
}
