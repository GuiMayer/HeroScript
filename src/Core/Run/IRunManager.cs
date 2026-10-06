using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Resources;
using System.Text.Json;

namespace Core.Run;

public interface IRunQueryService
{
    Result<RunState> GetRun(Guid runId);
    Result<RunState> GetRunByCombat(Guid combatId);
    Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(Guid runId);
}

public interface IRunCreationService
{
    Result<RunState> StartRun(RunStartOptions options);
}

/// <summary>
/// Public run application surface. Mutations after creation are commands and
/// therefore never appear on this interface.
/// </summary>
public interface IRunManager : IRunQueryService, IRunCreationService
{
    Result<IReadOnlyList<Core.Run.Content.CardTransformationOption>> GetCardTransformationOptions(Guid runId, Guid? cardInstanceId = null);
    Result<Core.Run.Content.CardTransformationAssessment> AssessCardTransformation(Guid runId, Guid cardInstanceId,
        CardTransformationOperation operation, ulong? transformationId = null, string? upgradeId = null);
}

/// <summary>
/// Internal aggregate boundary used by the combat transaction coordinator.
/// It is deliberately separate from the public query/creation surface.
/// </summary>
public interface IRunEncounterCommitter : IRunCombatResolutionCommitter
{
    Result<RunMapNodeState> ResolveCurrentNode(Guid runId, string currentNodeId);
    Result<RunMapNodeState> AdvanceNode(Guid runId, string targetNodeId);
    Result<RunState> AttachEncounter(
        Guid runId,
        int expectedSequence,
        ulong expectedStep,
        CombatState combatState,
        RunCommandIdentity? commandIdentity = null,
        RunState? initializedRun = null,
        RunEncounterStartCommand? initialCommand = null,
        CombatState? stateBeforeInitialization = null,
        CombatResolutionStep? initializationStep = null,
        JsonElement rootPayload = default);
    Result<RunState> ResolveEncounter(
        Guid runId,
        int expectedSequence,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null,
        JsonElement rootPayload = default);
}

public interface IRunEncounterRuntime :
    IRunQueryService,
    IRunEncounterCommitter,
    IRunCommandReceiptReader
{
}
