using System.Text.Json.Serialization;

namespace Core.Determinism;

/// <summary>
/// All ambient inputs that may influence a run. Advancing random state, logical
/// time, steps, or identifiers always creates a new context.
/// </summary>
public sealed record DeterministicContext
{
    public const string CurrentEngineVersion = "24";

    public ulong Seed { get; }
    public DeterministicRngState RandomState { get; }
    public ulong Step { get; }
    public ulong IdSequence { get; }
    public long LogicalTick { get; }
    public string ContentRevision { get; }
    public string EngineVersion { get; }

    [JsonConstructor]
    public DeterministicContext(
        ulong seed,
        DeterministicRngState randomState,
        ulong step,
        ulong idSequence,
        long logicalTick,
        string contentRevision,
        string engineVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(logicalTick);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);

        Seed = seed;
        RandomState = randomState;
        Step = step;
        IdSequence = idSequence;
        LogicalTick = logicalTick;
        ContentRevision = contentRevision;
        EngineVersion = engineVersion;
    }

    public static DeterministicContext Create(
        ulong seed,
        string contentRevision,
        string engineVersion = CurrentEngineVersion) =>
        new(
            seed,
            DeterministicRngState.FromSeed(seed),
            step: 0,
            idSequence: 0,
            logicalTick: 0,
            contentRevision,
            engineVersion);

    public DateTimeOffset LogicalTimestamp => DateTimeOffset.UnixEpoch.AddTicks(LogicalTick);

    public ContextValue<ulong> DrawUInt64()
    {
        var draw = DeterministicRng.NextUInt64(RandomState);
        return new ContextValue<ulong>(draw.Value, Copy(randomState: draw.NextState));
    }

    public ContextValue<int> DrawInt32(int exclusiveMaximum)
    {
        var draw = DeterministicRng.NextInt32(RandomState, exclusiveMaximum);
        return new ContextValue<int>(draw.Value, Copy(randomState: draw.NextState));
    }

    public ContextValue<double> DrawDouble()
    {
        var draw = DeterministicRng.NextDouble(RandomState);
        return new ContextValue<double>(draw.Value, Copy(randomState: draw.NextState));
    }

    public ContextValue<Guid> AllocateId(string scope)
    {
        var idScope = $"{EngineVersion}\n{ContentRevision}\n{scope}";
        var id = DeterministicId.Create(Seed, IdSequence, idScope);
        return new ContextValue<Guid>(id, Copy(idSequence: checked(IdSequence + 1)));
    }

    public DeterministicContext AdvanceStep(long logicalTicks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(logicalTicks);
        return Copy(
            step: checked(Step + 1),
            logicalTick: checked(LogicalTick + logicalTicks));
    }

    /// <summary>
    /// Changes the immutable content revision at an explicit, journaled
    /// transition. Callers must never use this as a silent reload mechanism.
    /// </summary>
    public DeterministicContext WithContentRevision(string contentRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRevision);
        return new DeterministicContext(
            Seed,
            RandomState,
            Step,
            IdSequence,
            LogicalTick,
            contentRevision,
            EngineVersion);
    }

    private DeterministicContext Copy(
        DeterministicRngState? randomState = null,
        ulong? step = null,
        ulong? idSequence = null,
        long? logicalTick = null) =>
        new(
            Seed,
            randomState ?? RandomState,
            step ?? Step,
            idSequence ?? IdSequence,
            logicalTick ?? LogicalTick,
            ContentRevision,
            EngineVersion);
}

/// <summary>
/// A value produced from a deterministic context and the context that must be
/// used by the next operation.
/// </summary>
public readonly record struct ContextValue<T>(T Value, DeterministicContext Context);
