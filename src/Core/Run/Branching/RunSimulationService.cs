using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Common;
using Core.Determinism;
using Core.Run.Runtime;

namespace Core.Run.Branching;

public sealed record SimulationCommand(string Type, JsonElement Payload);

public sealed record SimulationTimelineItem
{
    public int CommandIndex { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public int RunSequence { get; init; }
    public ulong? CombatStep { get; init; }
    public string StateHash { get; init; } = string.Empty;
}

public sealed record RunSimulationResult
{
    private ImmutableArray<SimulationTimelineItem> _timeline = [];

    public Guid SimulationId { get; init; }
    public Guid SourceRunId { get; init; }
    public int SourceSequence { get; init; }
    public string Status { get; init; } = "completed";
    public int CommandsExecuted { get; init; }
    public RunState FinalState { get; init; } = new();
    public string FinalStateHash { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, float> ResourceDeltas { get; init; } =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public int CardCountDelta { get; init; }
    public IReadOnlyList<SimulationTimelineItem> Timeline
    {
        get => _timeline;
        init => _timeline = value?.ToImmutableArray() ?? [];
    }
}

public interface IRunSimulationService
{
    Task<Result<RunSimulationResult>> ExecuteAsync(
        Guid sourceRunId,
        int sourceSequence,
        IReadOnlyList<SimulationCommand> commands,
        CancellationToken cancellationToken = default);
    Task<Result<RunSimulationResult>> GetAsync(
        Guid simulationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes theory-crafting commands on a dedicated durable branch. The source
/// aggregate is never hydrated into the command processor and cannot be changed.
/// </summary>
public sealed class RunSimulationService : IRunSimulationService
{
    private readonly IRunBranchService _branches;
    private readonly IRunCommitStore _repository;
    private readonly IGameplayRuntimeFactory _runtimeFactory;

    public RunSimulationService(
        IRunBranchService branches,
        IRunCommitStore repository,
        IGameplayRuntimeFactory runtimeFactory)
    {
        _branches = branches;
        _repository = repository;
        _runtimeFactory = runtimeFactory;
    }

    public async Task<Result<RunSimulationResult>> ExecuteAsync(
        Guid sourceRunId,
        int sourceSequence,
        IReadOnlyList<SimulationCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands == null)
            return Result<RunSimulationResult>.Failure("Simulation commands are required");
        if (commands.Any(command => string.IsNullOrWhiteSpace(command.Type)))
            return Result<RunSimulationResult>.Failure("Every simulation command requires a type");
        var source = await _repository.LoadStateAsync(sourceRunId, sourceSequence, cancellationToken).ConfigureAwait(false);
        if (source == null)
            return Result<RunSimulationResult>.Failure($"Run commit not found: {sourceRunId}/{sourceSequence}");
        var capability = source.ResolvedMode?.CapabilityPolicy;
        if (capability?.AllowCombatSimulation != true)
            return Result<RunSimulationResult>.Failure($"Game mode does not allow combat simulation: {source.ModeId}");
        if (commands.Count > capability.MaxSimulationCommands)
        {
            return Result<RunSimulationResult>.Failure(
                $"Simulation command limit exceeded: {capability.MaxSimulationCommands}");
        }
        var normalized = commands.Select(command => new
        {
            type = command.Type.Trim().ToUpperInvariant(),
            payload = command.Payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : command.Payload.Clone()
        }).ToArray();
        var commandHash = CanonicalJson.ComputeHash(normalized.Select(command => new
        {
            command.type,
            payloadHash = CanonicalJson.ComputeHash(command.payload)
        }).ToArray());
        var branch = await _branches.CreateAsync(
            sourceRunId,
            sourceSequence,
            $"simulation:{commandHash}",
            cancellationToken).ConfigureAwait(false);
        if (branch.IsFailure)
            return Result<RunSimulationResult>.Failure(branch.Error);

        var runtime = _runtimeFactory.Create(new GameplayRuntimeOptions(
            GameplayPersistenceMode.Authoritative,
            _repository));
        var loaded = runtime.Runs.GetRun(branch.Value.RunId);
        if (loaded.IsFailure)
            return Result<RunSimulationResult>.Failure(loaded.Error);
        var current = loaded.Value;
        var completedCommands = await CountCompletedCommandsAsync(current, cancellationToken)
            .ConfigureAwait(false);
        if (completedCommands > commands.Count)
            return Result<RunSimulationResult>.Failure("Simulation branch contains more commands than requested");
        for (var index = completedCommands; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = normalized[index].type;
            if (type is RunCommandTypes.StartEncounter or RunCommandTypes.RestoreHeadFromHistory)
                return Result<RunSimulationResult>.Failure($"Unsupported simulation command: {type}");
            var payload = normalized[index].payload;
            var payloadHash = CanonicalJson.ComputeHash(payload);
            var expectedStep = current.GetActiveEncounter()?.Combat.Determinism.Step ?? current.Determinism.Step;
            var identity = new RunCommandIdentity(
                CreateCommandId(current.RunId, index, type, payloadHash),
                type,
                current.Sequence,
                expectedStep,
                payloadHash);
            var executed = ExecuteCommand(runtime.Gateway, current, type, payload, identity);
            if (executed.IsFailure)
                return Result<RunSimulationResult>.Failure(
                    $"Simulation command {index} ({type}) failed: {executed.Error}");
            current = executed.Value;
        }

        return await BuildResultAsync(current, commands.Count, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RunSimulationResult>> GetAsync(
        Guid simulationId,
        CancellationToken cancellationToken = default)
    {
        var state = await _repository.LoadLatestStateAsync(simulationId, cancellationToken).ConfigureAwait(false);
        if (state?.Lineage?.ParentRunId == null || state.Lineage.SourceSequence == null ||
            !state.Lineage.InternalSimulation)
        {
            return Result<RunSimulationResult>.Failure($"Simulation not found: {simulationId}");
        }
        var commandsExecuted = await CountCompletedCommandsAsync(state, cancellationToken)
            .ConfigureAwait(false);
        return await BuildResultAsync(state, commandsExecuted, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<RunSimulationResult>> BuildResultAsync(
        RunState state,
        int commandsExecuted,
        CancellationToken cancellationToken)
    {
        var lineage = state.Lineage;
        if (lineage?.ParentRunId == null || lineage.SourceSequence == null)
            return Result<RunSimulationResult>.Failure("Simulation lineage is unavailable");
        var source = await _repository.LoadStateAsync(
            lineage.ParentRunId.Value,
            lineage.SourceSequence.Value,
            cancellationToken).ConfigureAwait(false);
        if (source == null)
            return Result<RunSimulationResult>.Failure("Simulation source commit is unavailable");
        return Result<RunSimulationResult>.Success(new RunSimulationResult
        {
            SimulationId = state.RunId,
            SourceRunId = source.RunId,
            SourceSequence = source.Sequence,
            CommandsExecuted = commandsExecuted,
            FinalState = state,
            FinalStateHash = CanonicalJson.ComputeHash(state),
            ResourceDeltas = state.ResourceState.Resources.Keys
                .Concat(source.ResourceState.Resources.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToDictionary(
                    id => id,
                    id => state.ResourceState.Current(id) - source.ResourceState.Current(id),
                    StringComparer.OrdinalIgnoreCase),
            CardCountDelta = CountCards(state.Deck) - CountCards(source.Deck),
            Timeline = await BuildTimelineAsync(state, commandsExecuted, cancellationToken).ConfigureAwait(false)
        });
    }

    private static Result<RunState> ExecuteCommand(
        IGameplayCommandGateway gateway,
        RunState current,
        string type,
        JsonElement payload,
        RunCommandIdentity identity)
    {
        var combatId = type is GameplayCommandTypes.PlayCard or
            GameplayCommandTypes.ExecuteAction or
            GameplayCommandTypes.EndTurn or
            RunCommandTypes.ResolveCombat
            ? current.GetActiveEncounter()?.Combat.CombatId
            : null;
        var executed = gateway.Execute(
            current.RunId,
            new GameplayCommandEnvelope(identity, payload),
            combatId);
        return executed.IsFailure
            ? Result<RunState>.Failure(executed.Error)
            : Result<RunState>.Success(executed.Value.Receipt.State);
    }

    private async Task<IReadOnlyList<SimulationTimelineItem>> BuildTimelineAsync(
        RunState state,
        int commandsExecuted,
        CancellationToken cancellationToken)
    {
        var entries = await _repository.LoadCommitsAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        return entries
            .Where(item => item.Sequence > 1)
            .OrderBy(item => item.Sequence)
            .Take(commandsExecuted)
            .Select((item, index) => new SimulationTimelineItem
            {
                CommandIndex = index,
                CommandType = item.RootCommand.Type,
                RunSequence = item.Sequence,
                CombatStep = item.StateAfter.GetActiveEncounter()?.Combat.Determinism.Step,
                StateHash = item.StateHash
            })
            .ToArray();
    }

    private async Task<int> CountCompletedCommandsAsync(
        RunState state,
        CancellationToken cancellationToken)
    {
        var journal = await _repository.LoadCommitsAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        return journal.Count(commit => commit.Sequence > 1);
    }

    private static int CountCards(DeckState deck) => deck.Topology.Instances.Count;

    private static Guid CreateCommandId(Guid runId, int index, string type, string payloadHash)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:N}:{index}:{type}:{payloadHash}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
