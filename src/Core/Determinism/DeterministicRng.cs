namespace Core.Determinism;

/// <summary>
/// Serializable state of the engine's deterministic pseudo-random generator.
/// The algorithm and its state are owned by the domain so replay does not depend
/// on the implementation of <see cref="System.Random"/> used by a runtime.
/// </summary>
public readonly record struct DeterministicRngState(ulong Value, ulong DrawCount)
{
    public static DeterministicRngState FromSeed(ulong seed) => new(seed, 0);
}

/// <summary>
/// A value produced by a pure random operation together with the next RNG state.
/// </summary>
public readonly record struct RandomDraw<T>(T Value, DeterministicRngState NextState);

/// <summary>
/// SplitMix64-based deterministic random operations. Every operation is pure:
/// callers must explicitly retain the returned state.
/// </summary>
public static class DeterministicRng
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    public static RandomDraw<ulong> NextUInt64(DeterministicRngState state)
    {
        var nextValue = unchecked(state.Value + GoldenGamma);
        var mixed = nextValue;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        mixed ^= mixed >> 31;

        return new RandomDraw<ulong>(
            mixed,
            new DeterministicRngState(nextValue, checked(state.DrawCount + 1)));
    }

    public static RandomDraw<int> NextInt32(DeterministicRngState state, int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);

        var bound = (ulong)exclusiveMaximum;
        var rejectionThreshold = unchecked(0UL - bound) % bound;
        var current = state;

        while (true)
        {
            var draw = NextUInt64(current);
            current = draw.NextState;

            if (draw.Value >= rejectionThreshold)
            {
                return new RandomDraw<int>((int)(draw.Value % bound), current);
            }
        }
    }

    public static RandomDraw<double> NextDouble(DeterministicRngState state)
    {
        var draw = NextUInt64(state);
        const double inverseTwoToThePowerOf53 = 1.0 / (1UL << 53);
        var value = (draw.Value >> 11) * inverseTwoToThePowerOf53;
        return new RandomDraw<double>(value, draw.NextState);
    }
}
