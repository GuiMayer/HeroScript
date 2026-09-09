using Core.Combat.Intents;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;

namespace Core.Combat.Flow;

/// <summary>
/// Resolves the run's pinned combat graph and composes focused boundary and
/// intent services. It never executes actions, controls AI or commits state.
/// </summary>
public sealed class CombatFlowPlanner : ICombatFlowPlanner
{
    private readonly IContentRuntimeResolver _contentRuntimes;
    private readonly ICombatBoundaryExecutor _boundaries;
    private readonly IIntentResolver _intents;

    public CombatFlowPlanner(
        IContentRuntimeResolver contentRuntimes,
        ICombatBoundaryExecutor boundaries,
        IIntentResolver intents)
    {
        _contentRuntimes = contentRuntimes ?? throw new ArgumentNullException(nameof(contentRuntimes));
        _boundaries = boundaries ?? throw new ArgumentNullException(nameof(boundaries));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
    }

    public Result<CombatState> Initialize(RunState run, CombatState combat)
    {
        var initialized = InitializeTransaction(run, combat);
        return initialized.IsFailure
            ? Result<CombatState>.Failure(initialized.Error)
            : Result<CombatState>.Success(initialized.Value.Combat);
    }

    public Result<CombatInitializationResult> InitializeTransaction(RunState run, CombatState combat)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        var context = ResolveContext(run);
        if (context.IsFailure)
            return Result<CombatInitializationResult>.Failure(context.Error);
        var initialized = _boundaries.InitializeTransaction(
            run,
            combat,
            context.Value.Sequence,
            context.Value.Policies,
            context.Value.TurnOrder);
        if (initialized.IsFailure || !initialized.Value.Combat.IsActive)
            return initialized;
        var published = PublishIntents(
            initialized.Value.Run,
            initialized.Value.Combat,
            context.Value.Policies);
        return published.IsFailure
            ? Result<CombatInitializationResult>.Failure(published.Error)
            : Result<CombatInitializationResult>.Success(WithCombat(initialized.Value, published.Value));
    }

    public Result<CombatRelicLifecycleResult> Complete(RunState run, CombatState combat) =>
        _boundaries.Complete(run, combat);

    public Result<CombatFlowAdvanceResult> AdvanceActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(runDeterminism);
        var context = ResolveContext(run);
        if (context.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(context.Error);
        var advanced = _boundaries.AdvanceActivation(
            run,
            combat,
            deck,
            runDeterminism,
            context.Value.Sequence,
            context.Value.Policies,
            context.Value.TurnOrder);
        if (advanced.IsFailure || !advanced.Value.Combat.IsActive)
            return advanced;
        var latestRun = advanced.Value.Steps.LastOrDefault(step => step.RunSnapshot != null)?.RunSnapshot ?? run;
        var published = PublishIntents(latestRun, advanced.Value.Combat, context.Value.Policies);
        if (published.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(published.Error);
        var steps = advanced.Value.Steps.ToArray();
        steps[^1] = steps[^1] with { Combat = published.Value };
        return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });
    }

    private Result<(
        PhaseSequenceDefinition Sequence,
        CombatFlowPoliciesDefinition Policies,
        TurnOrderPolicyDefinition TurnOrder)> ResolveContext(RunState run)
    {
        var combatRules = run.ResolvedMode?.CombatRules;
        if (combatRules == null)
        {
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition, TurnOrderPolicyDefinition)>.Failure(
                "Run has no resolved combat rules");
        }
        if (string.IsNullOrWhiteSpace(combatRules.DefaultPhaseSequenceId))
        {
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition, TurnOrderPolicyDefinition)>.Failure(
                "Combat rules have no phase sequence id");
        }

        var runtime = _contentRuntimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
        {
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition, TurnOrderPolicyDefinition)>.Failure(
                runtime.Error);
        }
        var sequence = runtime.Value.GetDefinition<PhaseSequenceDefinition>(
            "phase-sequences",
            combatRules.DefaultPhaseSequenceId);
        return sequence.IsFailure
            ? Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition, TurnOrderPolicyDefinition)>.Failure(
                sequence.Error)
            : Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition, TurnOrderPolicyDefinition)>.Success(
                (sequence.Value, combatRules.Flow, combatRules.TurnOrder));
    }

    private Result<CombatState> PublishIntents(
        RunState run,
        CombatState combat,
        CombatFlowPoliciesDefinition policies)
    {
        var activation = combat.ActivationState;
        if (activation == null)
            return Result<CombatState>.Failure("Combat activation has not been initialized");
        if (!policies.Ai.PublishIntents)
        {
            return Result<CombatState>.Success(combat with
            {
                ActivationState = activation with { Intents = [] }
            });
        }

        var resolved = _intents.ResolveEnemyIntents(
            run,
            combat,
            policies.Ai.DecisionIds,
            policies.Ai.Intent);
        return resolved.IsFailure
            ? Result<CombatState>.Failure(resolved.Error)
            : Result<CombatState>.Success(combat with
            {
                ActivationState = activation with { Intents = resolved.Value }
            });
    }

    private static CombatInitializationResult WithCombat(
        CombatInitializationResult initialized,
        CombatState combat) => initialized with
    {
        Combat = combat,
        Fingerprint = CanonicalJson.ComputeHash(new
        {
            combat,
            run = initialized.Run,
            effectSteps = initialized.EffectSteps,
            applications = initialized.Applications,
            phaseTransitions = initialized.PhaseTransitions
        })
    };
}
