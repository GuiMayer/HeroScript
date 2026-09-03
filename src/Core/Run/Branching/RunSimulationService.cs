using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Common;
using Core.Determinism;

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
    public int GoldDelta { get; init; }
    public int PowerPointsDelta { get; init; }
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
    private readonly IRunStateRepository _repository;
    private readonly IGameplayCommandGateway _commands;

    public RunSimulationService(
        IRunBranchService branches,
        IRunStateRepository repository,
        IGameplayCommandGateway commands)
    {
        _branches = branches;
        _repository = repository;
        _commands = commands;
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
        var source = await _repository.LoadAsync(sourceRunId, sourceSequence, cancellationToken).ConfigureAwait(false);
        if (source == null)
            return Result<RunSimulationResult>.Failure($"Run checkpoint not found: {sourceRunId}/{sourceSequence}");
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

        var current = branch.Value;
        var completedCommands = await CountCompletedCommandsAsync(current, cancellationToken)
            .ConfigureAwait(false);
        if (completedCommands > commands.Count)
            return Result<RunSimulationResult>.Failure("Simulation branch contains more commands than requested");
        for (var index = completedCommands; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = normalized[index].type;
            if (type is RunCommandTypes.StartEncounter or RunCommandTypes.RestoreCheckpoint)
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
            var executed = ExecuteCommand(current, type, payload, identity);
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
        var state = await _repository.LoadLatestAsync(simulationId, cancellationToken).ConfigureAwait(false);
        if (state == null || state.ParentRunId == null || state.BranchFromSequence == null ||
            state.BranchKey == null || !state.BranchKey.StartsWith("simulation:", StringComparison.Ordinal))
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
        var source = await _repository.LoadAsync(
            state.ParentRunId!.Value,
            state.BranchFromSequence!.Value,
            cancellationToken).ConfigureAwait(false);
        if (source == null)
            return Result<RunSimulationResult>.Failure("Simulation source checkpoint is unavailable");
        return Result<RunSimulationResult>.Success(new RunSimulationResult
        {
            SimulationId = state.RunId,
            SourceRunId = source.RunId,
            SourceSequence = source.Sequence,
            CommandsExecuted = commandsExecuted,
            FinalState = state,
            FinalStateHash = CanonicalJson.ComputeHash(state),
            GoldDelta = state.Gold - source.Gold,
            PowerPointsDelta = state.PowerPoints - source.PowerPoints,
            CardCountDelta = CountCards(state.Deck) - CountCards(source.Deck),
            Timeline = await BuildTimelineAsync(state, commandsExecuted, cancellationToken).ConfigureAwait(false)
        });
    }

    private Result<RunState> ExecuteCommand(
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
        var executed = _commands.Execute(
            current.RunId,
            new RunCommand(identity, payload),
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
        if (_repository is not IRunCheckpointRepository checkpoints)
            return [];
        var entries = await checkpoints.LoadCheckpointsAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        return entries
            .Where(item => item.State.Sequence > 1)
            .GroupBy(item => item.JournalEntry.RootCommandId ?? item.JournalEntry.CommandId)
            .Select(group => group.OrderBy(item => item.State.Sequence).Last())
            .OrderBy(item => item.State.Sequence)
            .Take(commandsExecuted)
            .Select((item, index) => new SimulationTimelineItem
            {
                CommandIndex = index,
                CommandType = item.JournalEntry.CommandType,
                RunSequence = item.State.Sequence,
                CombatStep = item.State.GetActiveEncounter()?.Combat.Determinism.Step,
                StateHash = item.JournalEntry.StateHash
            })
            .ToArray();
    }

    private async Task<int> CountCompletedCommandsAsync(
        RunState state,
        CancellationToken cancellationToken)
    {
        if (_repository is not IRunCheckpointRepository checkpoints)
            return System.Math.Max(0, state.Sequence - 1);
        var journal = await checkpoints.LoadCheckpointsAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        return journal
            .Select(checkpoint => checkpoint.JournalEntry)
            .Where(entry => entry.Sequence > 1)
            .Select(entry => entry.RootCommandId ?? entry.CommandId)
            .Where(commandId => commandId.HasValue)
            .Distinct()
            .Count();
    }

    private static int CountCards(DeckState deck) =>
        deck.DrawPile.Count + deck.Hand.Count + deck.DiscardPile.Count + deck.ExhaustPile.Count;

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
