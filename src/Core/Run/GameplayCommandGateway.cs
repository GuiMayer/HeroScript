using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Events;

namespace Core.Run;

public static class GameplayCommandTypes
{
    public const string PlayCard = "PLAY_CARD";
    public const string ExecuteAction = "EXECUTE_ACTION";
    public const string EndTurn = "END_TURN";
    public const string StartSandboxEncounter = "START_SANDBOX_ENCOUNTER";
}

public sealed record GameplayCommandResult(
    RunCommandReceipt Receipt,
    CombatState? CombatState = null);

public interface IGameplayCommandGateway
{
    Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId);
    Result<GameplayCommandResult> Execute(
        Guid runId,
        GameplayCommandEnvelope command,
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
    private readonly IGameplayCommandCodec _codec;

    public GameplayCommandGateway(
        IRunCommandProcessor commands,
        IRunManager runs,
        ICombatRunCoordinator combats,
        IGameEventContextAccessor eventContext,
        IGameplayCommandCodec? codec = null)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _combats = combats ?? throw new ArgumentNullException(nameof(combats));
        _eventContext = eventContext ?? throw new ArgumentNullException(nameof(eventContext));
        _codec = codec ?? GameplayCommandCodec.CreateDefault();
    }

    public Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId) =>
        _commands.FindReceipt(runId, commandId);

    public Result<GameplayCommandResult> Execute(
        Guid runId,
        GameplayCommandEnvelope command,
        Guid? combatId = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        var decodedResult = _codec.Decode(command);
        if (decodedResult.IsFailure)
            return Result<GameplayCommandResult>.Failure(decodedResult.Error);
        var decoded = decodedResult.Value;
        command = decoded.Envelope;
        if (combatId.HasValue && decoded.Descriptor.Route is not GameplayCommandRoute.Combat
            and not GameplayCommandRoute.ResolveEncounter)
        {
            return Result<GameplayCommandResult>.Failure(
                $"Command {command.Identity.Type} is not valid on a combat endpoint");
        }

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

        return decoded.Descriptor.Route switch
        {
            GameplayCommandRoute.StartEncounter =>
                StartEncounter(runId, command, (StartEncounterCommand)decoded.Payload),
            GameplayCommandRoute.SandboxEncounter =>
                StartSandboxEncounter(runId, command, (StartSandboxEncounterCommand)decoded.Payload),
            GameplayCommandRoute.ResolveEncounter =>
                ResolveEncounter(runId, combatId, command, (ResolveCombatCommand)decoded.Payload),
            GameplayCommandRoute.Combat =>
                ExecuteCombatAction(runId, combatId, command, (CombatGameplayCommand)decoded.Payload),
            GameplayCommandRoute.Run => ExecuteRunCommand(runId, command),
            _ => Result<GameplayCommandResult>.Failure(
                $"Unsupported command route: {decoded.Descriptor.Route}")
        };
    }

    private Result<GameplayCommandResult> ExecuteRunCommand(Guid runId, GameplayCommandEnvelope command)
    {
        var executed = _commands.Execute(runId, command);
        return executed.IsSuccess
            ? Result<GameplayCommandResult>.Success(new GameplayCommandResult(executed.Value))
            : Result<GameplayCommandResult>.Failure(executed.Error);
    }

    private Result<GameplayCommandResult> StartEncounter(
        Guid runId,
        GameplayCommandEnvelope command,
        StartEncounterCommand payload)
    {
        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);
        var started = _combats.StartEncounter(
            runId,
            payload.Hero,
            payload.Enemies,
            payload.InitialResourceValues,
            command.Identity,
            command.Payload);
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
        GameplayCommandEnvelope command,
        ResolveCombatCommand payload)
    {
        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);
        var resolvedCombatId = combatId ?? payload.CombatId;
        if (resolvedCombatId == Guid.Empty)
            return Result<GameplayCommandResult>.Failure("CombatId is required for RESOLVE_COMBAT");
        var resolved = _combats.ResolveEncounter(
            runId,
            resolvedCombatId,
            command.Identity,
            command.Payload);
        if (resolved.IsFailure)
            return Result<GameplayCommandResult>.Failure(resolved.Error);
        return LoadReceipt(
            runId,
            command.Identity.CommandId,
            resolved.Value.CombatState,
            duplicate.Value != null);
    }

    private Result<GameplayCommandResult> StartSandboxEncounter(
        Guid runId,
        GameplayCommandEnvelope command,
        StartSandboxEncounterCommand payload)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return Result<GameplayCommandResult>.Failure(run.Error);
        if (run.Value.ResolvedMode?.CapabilityPolicy.AllowScenarioAuthoring != true)
            return Result<GameplayCommandResult>.Failure("Game mode does not allow sandbox scenario authoring");

        var duplicate = _commands.FindReceipt(runId, command.Identity.CommandId);
        if (duplicate.IsFailure)
            return Result<GameplayCommandResult>.Failure(duplicate.Error);
        var started = _combats.StartEncounter(
            runId,
            payload.Hero,
            payload.Enemies,
            command.Identity,
            payload.InitialStatusEffects,
            command.Payload);
        if (started.IsFailure)
            return Result<GameplayCommandResult>.Failure(started.Error);
        return LoadReceipt(
            runId,
            command.Identity.CommandId,
            started.Value.CombatState,
            duplicate.Value != null);
    }

    private Result<GameplayCommandResult> ExecuteCombatAction(
        Guid runId,
        Guid? combatId,
        GameplayCommandEnvelope command,
        CombatGameplayCommand payload)
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

        var built = BuildCombatAction(command.Identity.Type, payload, run.Value, combat);
        if (built.IsFailure)
            return Result<GameplayCommandResult>.Failure(built.Error);
        var executed = _combats.ExecuteAction(
            combatId.Value,
            built.Value,
            command.Identity,
            command.Payload);
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
        CombatGameplayCommand request,
        RunState run,
        CombatState combat)
    {
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

}
