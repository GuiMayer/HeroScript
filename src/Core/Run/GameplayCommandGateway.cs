using System.Text.Json;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Events;

namespace Core.Run;

public static class GameplayCommandTypes
{
    public const string PlayCard = "PLAY_CARD";
    public const string ExecuteAction = "EXECUTE_ACTION";
    public const string EndTurn = "END_TURN";
}

public sealed record GameplayCommandResult(
    RunCommandReceipt Receipt,
    CombatState? CombatState = null);

public interface IGameplayCommandGateway
{
    Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId);
    Result<GameplayCommandResult> Execute(
        Guid runId,
        RunCommand command,
        Guid? combatId = null);
}

/// <summary>
/// Single mutation gateway shared by HTTP, simulations and other adapters.
/// It owns command normalization and routes aggregate-spanning transitions to
/// the combat coordinator while ordinary transitions remain in RunManager.
/// </summary>
public sealed class GameplayCommandGateway : IGameplayCommandGateway
{
    private readonly IRunCommandProcessor _commands;
    private readonly IRunManager _runs;
    private readonly ICombatRunCoordinator _combats;
    private readonly IGameEventContextAccessor _eventContext;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public GameplayCommandGateway(
        IRunCommandProcessor commands,
        IRunManager runs,
        ICombatRunCoordinator combats,
        IGameEventContextAccessor eventContext)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _combats = combats ?? throw new ArgumentNullException(nameof(combats));
        _eventContext = eventContext ?? throw new ArgumentNullException(nameof(eventContext));
    }

    public Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId) =>
        _commands.FindReceipt(runId, commandId);

    public Result<GameplayCommandResult> Execute(
        Guid runId,
        RunCommand command,
        Guid? combatId = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalized = Normalize(command);
        if (normalized.IsFailure)
            return Result<GameplayCommandResult>.Failure(normalized.Error);
        command = normalized.Value;

        var currentRun = _runs.GetRun(runId);
        using var contextScope = _eventContext.Push(new GameEventContext
        {
            CorrelationId = command.Identity.CommandId,
            RunId = runId,
            CombatId = combatId,
            CommandId = command.Identity.CommandId,
            ExpectedRunSequence = command.Identity.ExpectedSequence,
            ExpectedRunStep = command.Identity.ExpectedStep,
            ContentRevision = currentRun is { IsSuccess: true }
                ? currentRun.Value.Determinism.ContentRevision
                : string.Empty
        });

        try
        {
            return command.Identity.Type switch
            {
                RunCommandTypes.StartEncounter => StartEncounter(runId, command),
                RunCommandTypes.ResolveCombat => ResolveEncounter(runId, combatId, command),
                GameplayCommandTypes.PlayCard or GameplayCommandTypes.ExecuteAction or GameplayCommandTypes.EndTurn =>
                    ExecuteCombatAction(runId, combatId, command),
                _ => ExecuteRunCommand(runId, command)
            };
        }
        catch (JsonException exception)
        {
            return Result<GameplayCommandResult>.Failure(
                $"Invalid payload for {command.Identity.Type}: {exception.Message}");
        }
    }

    private Result<GameplayCommandResult> ExecuteRunCommand(Guid runId, RunCommand command)
    {
        var executed = _commands.Execute(runId, command);
        return executed.IsSuccess
            ? Result<GameplayCommandResult>.Success(new GameplayCommandResult(executed.Value))
            : Result<GameplayCommandResult>.Failure(executed.Error);
    }

    private Result<GameplayCommandResult> StartEncounter(Guid runId, RunCommand command)
    {
        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);
        var payload = command.Payload.Deserialize<StartEncounterCommandPayload>(_jsonOptions)
            ?? throw new JsonException("START_ENCOUNTER payload is required");
        var started = _combats.StartEncounter(
            runId,
            payload.HeroId,
            payload.EnemyIds,
            payload.InitialEnergy,
            command.Identity);
        if (started.IsFailure)
            return Result<GameplayCommandResult>.Failure(started.Error);
        return LoadReceipt(
            runId,
            command.Identity.CommandId,
            started.Value.CombatState,
            duplicate.Value != null);
    }

    private Result<GameplayCommandResult> ResolveEncounter(
        Guid runId,
        Guid? combatId,
        RunCommand command)
    {
        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);
        var payload = command.Payload.Deserialize<ResolveCombatCommandPayload>(_jsonOptions);
        var resolvedCombatId = combatId ?? payload?.CombatId ?? Guid.Empty;
        if (resolvedCombatId == Guid.Empty)
            return Result<GameplayCommandResult>.Failure("CombatId is required for RESOLVE_COMBAT");
        var resolved = _combats.ResolveEncounter(runId, resolvedCombatId, command.Identity);
        if (resolved.IsFailure)
            return Result<GameplayCommandResult>.Failure(resolved.Error);
        return LoadReceipt(
            runId,
            command.Identity.CommandId,
            resolved.Value.CombatState,
            duplicate.Value != null);
    }

    private Result<GameplayCommandResult> ExecuteCombatAction(
        Guid runId,
        Guid? combatId,
        RunCommand command)
    {
        if (!combatId.HasValue || combatId == Guid.Empty)
            return Result<GameplayCommandResult>.Failure("CombatId is required for combat commands");
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return Result<GameplayCommandResult>.Failure(run.Error);
        var combat = run.Value.GetEncounter(combatId.Value)?.Combat;
        if (combat == null)
            return Result<GameplayCommandResult>.Failure($"Combat not found: {combatId}");
        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);

        var built = BuildCombatAction(command.Identity.Type, command.Payload, run.Value, combat);
        if (built.IsFailure)
            return Result<GameplayCommandResult>.Failure(built.Error);
        var executed = _combats.ExecuteAction(combatId.Value, built.Value, command.Identity);
        if (executed.IsFailure)
            return Result<GameplayCommandResult>.Failure(executed.Error);
        return LoadReceipt(
            runId,
            command.Identity.CommandId,
            executed.Value.CombatState,
            duplicate.Value != null);
    }

    private Result<CombatActionCommand> BuildCombatAction(
        string type,
        JsonElement payload,
        RunState run,
        CombatState combat)
    {
        var request = payload.Deserialize<CombatGameplayCommandPayload>(_jsonOptions) ?? new();
        if (type == GameplayCommandTypes.EndTurn)
        {
            return Result<CombatActionCommand>.Success(new CombatActionCommand
            {
                ActorId = request.ActorId ?? combat.Hero.EntityId,
                ActionType = ActionType.END_TURN,
                RunId = run.RunId
            });
        }

        if (type == GameplayCommandTypes.PlayCard)
        {
            if (!request.CardInstanceId.HasValue || request.CardInstanceId == Guid.Empty)
                return Result<CombatActionCommand>.Failure("CardInstanceId is required for PLAY_CARD");
            return Result<CombatActionCommand>.Success(new CombatActionCommand
            {
                ActorId = request.ActorId ?? combat.Hero.EntityId,
                ActionType = ActionType.PLAY_CARD,
                CardInstanceId = request.CardInstanceId,
                TargetIds = request.TargetIds ?? [],
                CostOptionId = request.CostOptionId,
                RunId = run.RunId
            });
        }

        var actionType = request.ActionType;
        var powerId = request.PowerId;
        if (!actionType.HasValue)
            return Result<CombatActionCommand>.Failure("ActionType is required for EXECUTE_ACTION");

        return Result<CombatActionCommand>.Success(new CombatActionCommand
        {
            ActorId = request.ActorId ?? combat.Hero.EntityId,
            ActionType = actionType.Value,
            PowerId = powerId,
            TargetId = request.TargetId,
            CostOptionId = request.CostOptionId,
            RunId = run.RunId
        });
    }

    private Result<GameplayCommandResult> LoadReceipt(
        Guid runId,
        Guid commandId,
        CombatState? combat,
        bool duplicate)
    {
        var receipt = _commands.FindReceipt(runId, commandId);
        if (receipt.IsFailure)
            return Result<GameplayCommandResult>.Failure(receipt.Error);
        if (receipt.Value == null)
            return Result<GameplayCommandResult>.Failure("Command completed without a durable receipt");
        return Result<GameplayCommandResult>.Success(new GameplayCommandResult(
            receipt.Value with { Duplicate = duplicate },
            combat));
    }

    private static Result<RunCommand> Normalize(RunCommand command)
    {
        if (command.Identity.CommandId == Guid.Empty)
            return Result<RunCommand>.Failure("Command id is required");
        if (string.IsNullOrWhiteSpace(command.Identity.Type))
            return Result<RunCommand>.Failure("Command type is required");
        var payload = command.Payload.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new { })
            : command.Payload.Clone();
        var payloadHash = CanonicalJson.ComputeHash(payload);
        if (!string.IsNullOrWhiteSpace(command.Identity.PayloadHash) &&
            !string.Equals(command.Identity.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return Result<RunCommand>.Failure("Command payload hash does not match its canonical payload");
        }

        return Result<RunCommand>.Success(command with
        {
            Identity = command.Identity with
            {
                Type = command.Identity.Type.Trim().ToUpperInvariant(),
                PayloadHash = payloadHash
            },
            Payload = payload
        });
    }

    private sealed record StartEncounterCommandPayload(
        string HeroId,
        IReadOnlyList<string> EnemyIds,
        int InitialEnergy = 3);

    private sealed record ResolveCombatCommandPayload(Guid CombatId);

    private sealed record CombatGameplayCommandPayload(
        string? ActorId = null,
        ActionType? ActionType = null,
        string? PowerId = null,
        string? TargetId = null,
        IReadOnlyList<string>? TargetIds = null,
        string? CostOptionId = null,
        Guid? CardInstanceId = null);
}
