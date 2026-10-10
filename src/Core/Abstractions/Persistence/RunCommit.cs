using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Branching;
using Core.Meta;

namespace Core.Abstractions.Persistence;

public sealed record RunCommitFrame
{
    private ImmutableArray<EffectExecutionStep> _effectSteps = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<CardZoneFlowStepRecord> _cardZoneSteps = [];

    public int FrameIndex { get; init; }
    public ulong Step { get; init; }
    public string Scope { get; init; } = "run";
    public string Kind { get; init; } = string.Empty;
    public Guid? CombatId { get; init; }
    public string? ActorId { get; init; }
    public string? PhaseId { get; init; }
    public int? Round { get; init; }
    public int? Activation { get; init; }
    public string ResultHash { get; init; } = string.Empty;
    public JsonElement Resolution { get; init; }
    public Guid? ResolutionFrameId { get; init; }
    public ulong? CombatStep { get; init; }
    public int? SnapshotSequence { get; init; }
    public CombatState? CombatStateAfter { get; init; }
    public IReadOnlyList<EffectExecutionStep> EffectSteps
    {
        get => _effectSteps;
        init => _effectSteps = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneFlowStepRecord> CardZoneSteps
    {
        get => _cardZoneSteps;
        init => _cardZoneSteps = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// Compact header for a combat resolution. Frame details live on the commit
/// frames so timeline, replay and resolution queries share one authority.
/// </summary>
public sealed record RunCommitCombatResolution
{
    public Guid CommandId { get; init; }
    public Guid CombatId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public AnimationFrameMode Mode { get; init; }
    public int RootSequence { get; init; }
    public string InitialCombatStateHash { get; init; } = string.Empty;
    public string FinalCombatStateHash { get; init; } = string.Empty;
    public string ResolutionFingerprint { get; init; } = string.Empty;
}

public sealed record RunCommitFact
{
    public int FactIndex { get; init; }
    public ulong Step { get; init; }
    public string Scope { get; init; } = "run";
    public string Type { get; init; } = string.Empty;
    public Guid? CombatId { get; init; }
    public string? ActorId { get; init; }
    public string? PhaseId { get; init; }
    public int? Round { get; init; }
    public int? Activation { get; init; }
    public JsonElement Payload { get; init; }
}

/// <summary>
/// The single authoritative persistence unit for one externally observable
/// gameplay command. Internal transitions are immutable frames in this commit.
/// </summary>
public sealed record RunCommit
{
    public const int CurrentSchemaVersion = 4;

    private ImmutableArray<RunCommitFrame> _frames = [];
    private ImmutableArray<RunCommitFact> _facts = [];
    private ImmutableArray<RunStatePatchOperation> _stateDelta = [];

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string EngineVersion { get; init; } = DeterministicContext.CurrentEngineVersion;
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public RunCommandIdentity RootCommand { get; init; } = null!;
    public JsonElement Command { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public ulong BeforeStep { get; init; }
    public ulong AfterStep { get; init; }
    public DateTime LogicalTimestamp { get; init; } = DateTime.UnixEpoch;
    public RunCommitStorageKind StorageKind { get; init; } = RunCommitStorageKind.Checkpoint;
    public RunState? StateAfter { get; init; }
    public RunLineage? Lineage { get; init; }
    public RunCommitCombatResolution? CombatResolution { get; init; }
    public ProfileProgressCommit? ProfileProgress { get; init; }

    public IReadOnlyList<RunCommitFrame> Frames
    {
        get => _frames;
        init => _frames = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<RunCommitFact> Facts
    {
        get => _facts;
        init => _facts = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<RunStatePatchOperation> StateDelta
    {
        get => _stateDelta;
        init => _stateDelta = value?.ToImmutableArray() ?? [];
    }

    public void Validate(string? computedStateHash = null)
    {
        ValidateEnvelope();
        if (StateAfter == null)
            throw new InvalidOperationException("Run commit must be materialized before state validation");

        if (StateAfter.RunId != RunId || StateAfter.Sequence != Sequence)
            throw new InvalidOperationException("Run commit state identity does not match its sequence");
        if (StateAfter.Determinism.Step != AfterStep)
            throw new InvalidOperationException("Run commit deterministic step range is invalid");
        var actualStateHash = computedStateHash ?? CanonicalJson.ComputeHash(StateAfter);
        if (!string.Equals(actualStateHash, StateHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Run commit stateHash does not match stateAfter");
        if (Sequence == 1 && StateAfter.Lineage != Lineage)
            throw new InvalidOperationException("Initial commit lineage differs from state lineage");
        if (ProfileProgress is { } progress && (progress.PlayerId != StateAfter.PlayerEntityId ||
            progress.SettingId != StateAfter.SettingId || progress.ContentRevision != StateAfter.Determinism.ContentRevision ||
            StateAfter.Lineage?.InternalSimulation == true || StateAfter.ResolvedMode?.ProfileProgressPolicy == null ||
            CanonicalJson.ComputeHash(progress.Policy) != CanonicalJson.ComputeHash(StateAfter.ResolvedMode.ProfileProgressPolicy)))
            throw new InvalidOperationException("Profile progress differs from its authoritative run scope/policy");
    }

    public RunState RequireState() => StateAfter
        ?? throw new InvalidOperationException(
            $"Run commit state is not materialized: {RunId}/{Sequence}");

    public void ValidateEnvelope()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported run commit schema: {SchemaVersion}");
        if (!string.Equals(EngineVersion, DeterministicContext.CurrentEngineVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported run commit engine: {EngineVersion}");
        if (RunId == Guid.Empty || RootCommand == null)
            throw new InvalidOperationException("Run commit identity and command are required");
        if (Sequence < 1)
            throw new InvalidOperationException("Run commit sequence must be positive");
        if (RootCommand.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(RootCommand.Type))
            throw new InvalidOperationException("Run commit root command is invalid");
        if (AfterStep < BeforeStep)
            throw new InvalidOperationException("Run commit deterministic step range is invalid");
        if (Sequence > 1 && string.IsNullOrWhiteSpace(PreviousStateHash))
            throw new InvalidOperationException("Non-initial run commit requires previousStateHash");
        if (string.IsNullOrWhiteSpace(StateHash))
            throw new InvalidOperationException("Run commit stateHash is required");
        if (StorageKind == RunCommitStorageKind.Checkpoint && StateAfter == null)
            throw new InvalidOperationException("Checkpoint run commit requires stateAfter");
        if (StorageKind == RunCommitStorageKind.Checkpoint && _stateDelta.Length != 0)
            throw new InvalidOperationException("Checkpoint run commit cannot contain a state delta");
        if (StorageKind == RunCommitStorageKind.Delta && StateAfter == null && _stateDelta.Length == 0)
            throw new InvalidOperationException("Delta run commit requires state changes");
        if (Sequence == 1)
        {
            if (StorageKind != RunCommitStorageKind.Checkpoint)
                throw new InvalidOperationException("Initial run commit must be a checkpoint");
            if (Lineage == null)
                throw new InvalidOperationException("Initial run commit requires lineage");
            var lineageValidation = Lineage.Validate(RunId);
            if (lineageValidation.IsFailure)
                throw new InvalidOperationException(lineageValidation.Error);
        }
        else if (Lineage != null)
        {
            throw new InvalidOperationException("Only the initial run commit may anchor lineage");
        }

        var commandHash = CanonicalJson.ComputeHash(
            Command.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : Command);
        if (!string.Equals(commandHash, RootCommand.PayloadHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Run commit command payload hash does not match its envelope");
        if (_frames.Length == 0 || _facts.Length == 0)
            throw new InvalidOperationException("Run commit requires at least one frame and one durable fact");
        ProfileProgress?.ValidateEnvelope(RunId, Sequence, RootCommand.CommandId);

        if (CombatResolution is { } resolution &&
            (resolution.CommandId != RootCommand.CommandId ||
             resolution.CombatId == Guid.Empty ||
             !string.Equals(resolution.CommandType, RootCommand.Type, StringComparison.Ordinal) ||
             resolution.RootSequence != Sequence ||
             string.IsNullOrWhiteSpace(resolution.InitialCombatStateHash) ||
             string.IsNullOrWhiteSpace(resolution.FinalCombatStateHash) ||
             string.IsNullOrWhiteSpace(resolution.ResolutionFingerprint)))
        {
            throw new InvalidOperationException("Run commit combat resolution header is invalid");
        }

        for (var index = 0; index < _frames.Length; index++)
        {
            var frame = _frames[index];
            if (frame.FrameIndex != index || frame.Step < BeforeStep || frame.Step > AfterStep ||
                string.IsNullOrWhiteSpace(frame.Scope) || string.IsNullOrWhiteSpace(frame.Kind))
                throw new InvalidOperationException($"Invalid run commit frame at index {index}");
            if (index > 0 && frame.Step < _frames[index - 1].Step)
                throw new InvalidOperationException("Run commit frame steps must be monotonic");
        }

        for (var index = 0; index < _facts.Length; index++)
        {
            var fact = _facts[index];
            if (fact.FactIndex != index || string.IsNullOrWhiteSpace(fact.Scope) ||
                string.IsNullOrWhiteSpace(fact.Type) || fact.Step < BeforeStep || fact.Step > AfterStep)
                throw new InvalidOperationException($"Invalid run commit fact at index {index}");
            if (index > 0 && fact.Step < _facts[index - 1].Step)
                throw new InvalidOperationException("Run commit fact steps must be monotonic");
        }
    }

    public RunJournalEntry ToJournalEntry() => new()
    {
        RunId = RunId,
        CommandId = RootCommand.CommandId,
        RootCommandId = RootCommand.CommandId,
        TransitionIndex = 0,
        TransitionCount = _frames.Length == 0 ? 1 : _frames.Length,
        Sequence = Sequence,
        Step = AfterStep,
        ExpectedSequence = RootCommand.ExpectedSequence,
        ExpectedStep = RootCommand.ExpectedStep,
        CommandPayloadHash = RootCommand.PayloadHash,
        CommandType = RootCommand.Type,
        Command = Command.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new { })
            : Command.Clone(),
        PreviousStateHash = PreviousStateHash,
        StateHash = StateHash,
        LogicalTimestamp = LogicalTimestamp
    };
}

public enum RunCommitStorageKind
{
    Checkpoint,
    Delta
}

/// <summary>
/// Immutable serialized form used by the append path. Preparing once keeps
/// validation, hashing and persistence on the same canonical bytes.
/// </summary>
public sealed class PreparedRunCommit
{
    private PreparedRunCommit(RunCommit commit, RunState liveState, byte[] bytes, string byteHash, RunState? previousState)
    {
        Commit = commit;
        LiveState = liveState;
        Bytes = bytes;
        ByteHash = byteHash;
        PreviousState = previousState;
    }

    public RunCommit Commit { get; }
    public RunState LiveState { get; }
    public RunState? PreviousState { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public string ByteHash { get; }

    public static PreparedRunCommit Create(RunCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var stateHash = CanonicalJson.ComputeHash(commit.StateAfter);
        return CreateVerified(commit, stateHash);
    }

    public static PreparedRunCommit CreateVerified(RunCommit commit, string computedStateHash)
    {
        return CreateVerified(commit, computedStateHash, previousState: null);
    }

    public static PreparedRunCommit CreateVerified(
        RunCommit commit,
        string computedStateHash,
        RunState? previousState)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(computedStateHash);
        var liveState = commit.StateAfter
            ?? throw new InvalidOperationException("Preparing a run commit requires its live state");
        commit.Validate(computedStateHash);
        var checkpoint = previousState == null || RunCommitCheckpointPolicy.ShouldCheckpoint(commit);
        var persisted = checkpoint
            ? commit with
            {
                StorageKind = RunCommitStorageKind.Checkpoint,
                StateDelta = []
            }
            : commit with
            {
                StorageKind = RunCommitStorageKind.Delta,
                StateAfter = null,
                StateDelta = RunStateDelta.Create(previousState!, liveState).Operations
            };
        persisted.ValidateEnvelope();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(persisted, RunCommitJson.Options);
        return new PreparedRunCommit(
            persisted,
            liveState,
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)), previousState);
    }

    public PreparedRunCommit WithProfileProgress(ProfileProgressCommit progress)
    {
        var commit = Commit with { ProfileProgress = progress };
        (commit with { StateAfter = LiveState }).Validate(commit.StateHash);
        commit.ValidateEnvelope();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(commit, RunCommitJson.Options);
        return new(commit, LiveState, bytes, Convert.ToHexString(SHA256.HashData(bytes)), PreviousState);
    }
}

public static class RunCommitCheckpointPolicy
{
    public const int Interval = 25;

    public static bool ShouldCheckpoint(RunCommit commit) =>
        commit.Sequence == 1 ||
        commit.Sequence % Interval == 0 ||
        string.Equals(commit.RootCommand.Type, RunCommandTypes.StartEncounter, StringComparison.Ordinal) ||
        string.Equals(commit.RootCommand.Type, RunCommandTypes.ResolveCombat, StringComparison.Ordinal) ||
        string.Equals(commit.RootCommand.Type, RunCommandTypes.AbandonRun, StringComparison.Ordinal);
}

public static class RunCommitJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record RunCommitAppendResult(RunCommit Commit, bool Duplicate);

public static class RunCommitFacts
{
    public const string TransitionCommitted = "RUN_TRANSITION_COMMITTED";

    public static IReadOnlyList<RunCommitFact> FromFrames(IReadOnlyList<RunCommitFrame> frames) =>
        frames.Select((frame, index) => new RunCommitFact
        {
            FactIndex = index,
            Step = frame.Step,
            Scope = frame.Scope,
            Type = TransitionCommitted,
            CombatId = frame.CombatId,
            ActorId = frame.ActorId,
            PhaseId = frame.PhaseId,
            Round = frame.Round,
            Activation = frame.Activation,
            Payload = frame.Resolution.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : frame.Resolution.Clone()
        }).ToArray();
}
