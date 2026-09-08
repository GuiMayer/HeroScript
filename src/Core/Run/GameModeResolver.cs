using Core.Common;
using Core.Combat.Flow;
using Core.Config;
using Core.Content;
using Core.Logging;
using Core.Run.Content;

namespace Core.Run;

public interface IGameModeResolver
{
    Result<ResolvedGameMode> Resolve(string modeId, string configName);
}

public interface IRevisionedGameModeResolver
{
    Result<ResolvedGameMode> Resolve(string modeId, string configName, string contentRevision);
}

/// <summary>
/// Resolves the policy graph selected by a mode before a run starts. Keeping
/// this at the engine boundary prevents clients from enabling capabilities by
/// merely sending different JSON fields.
/// </summary>
public sealed class GameModeResolver : IGameModeResolver, IRevisionedGameModeResolver
{
    private readonly IResourceCatalog<GameModeDefinition> _modes;
    private readonly IResourceCatalog<FlowRulesDefinition> _flowRules;
    private readonly IResourceCatalog<CombatRulesDefinition> _combatRules;
    private readonly IResourceCatalog<ReplayPolicyDefinition> _replayPolicies;
    private readonly IResourceCatalog<TimelinePolicyDefinition> _timelinePolicies;
    private readonly IResourceCatalog<ContentBindingPolicyDefinition> _contentBindingPolicies;
    private readonly IResourceCatalog<CapabilityPolicyDefinition> _capabilityPolicies;
    private readonly ICardPoolResolver? _cardPools;
    private readonly IResourceCatalog<EnemyPoolDefinition>? _enemyPools;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly ILogger? _logger;

    public GameModeResolver(
        IResourceCatalog<GameModeDefinition> modes,
        IResourceCatalog<FlowRulesDefinition> flowRules,
        IResourceCatalog<CombatRulesDefinition> combatRules,
        IResourceCatalog<ReplayPolicyDefinition> replayPolicies,
        IResourceCatalog<TimelinePolicyDefinition> timelinePolicies,
        IResourceCatalog<ContentBindingPolicyDefinition> contentBindingPolicies,
        IResourceCatalog<CapabilityPolicyDefinition> capabilityPolicies,
        ICardPoolResolver? cardPools = null,
        IResourceCatalog<EnemyPoolDefinition>? enemyPools = null,
        IContentRuntimeResolver? contentRuntimes = null,
        ILogger? logger = null)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _flowRules = flowRules ?? throw new ArgumentNullException(nameof(flowRules));
        _combatRules = combatRules ?? throw new ArgumentNullException(nameof(combatRules));
        _replayPolicies = replayPolicies ?? throw new ArgumentNullException(nameof(replayPolicies));
        _timelinePolicies = timelinePolicies ?? throw new ArgumentNullException(nameof(timelinePolicies));
        _contentBindingPolicies = contentBindingPolicies ?? throw new ArgumentNullException(nameof(contentBindingPolicies));
        _capabilityPolicies = capabilityPolicies ?? throw new ArgumentNullException(nameof(capabilityPolicies));
        _cardPools = cardPools;
        _enemyPools = enemyPools;
        _contentRuntimes = contentRuntimes;
        _logger = logger;
    }

    public Result<ResolvedGameMode> Resolve(string modeId, string configName)
    {
        var mode = _modes.Get(modeId, configName);
        if (mode.IsFailure)
            return Result<ResolvedGameMode>.Failure(mode.Error);
        if (!string.Equals(mode.Value.ModeId, modeId, StringComparison.Ordinal))
            return Result<ResolvedGameMode>.Failure($"Game mode definition identity mismatch: {modeId}");

        var flow = GetRequired(_flowRules, mode.Value.FlowRulesId, "flow rules", configName);
        var combat = GetRequired(_combatRules, mode.Value.CombatRulesId, "combat rules", configName);
        var replay = GetRequired(_replayPolicies, mode.Value.ReplayPolicyId, "replay policy", configName);
        var timeline = GetRequired(_timelinePolicies, mode.Value.TimelinePolicyId, "timeline policy", configName);
        var binding = GetRequired(
            _contentBindingPolicies,
            mode.Value.ContentBindingPolicyId,
            "content binding policy",
            configName);
        var capabilities = GetRequired(
            _capabilityPolicies,
            mode.Value.CapabilityPolicyId,
            "capability policy",
            configName);

        if (flow.IsFailure || combat.IsFailure || replay.IsFailure || timeline.IsFailure ||
            binding.IsFailure || capabilities.IsFailure)
        {
            var errors = new[]
            {
                flow.IsFailure ? flow.Error : null,
                combat.IsFailure ? combat.Error : null,
                replay.IsFailure ? replay.Error : null,
                timeline.IsFailure ? timeline.Error : null,
                binding.IsFailure ? binding.Error : null,
                capabilities.IsFailure ? capabilities.Error : null
            };
            return Result<ResolvedGameMode>.Failure(string.Join("; ", errors.Where(error => error != null)));
        }

        WarnReservedPolicies(combat.Value);
        var policyValidation = ValidatePolicies(
            combat.Value,
            replay.Value,
            timeline.Value,
            binding.Value,
            capabilities.Value);
        if (policyValidation.IsFailure)
            return Result<ResolvedGameMode>.Failure(policyValidation.Error);

        if (_cardPools != null)
        {
            foreach (var poolId in mode.Value.CardPoolIds)
            {
                var pool = _cardPools.GetPool(poolId, configName);
                if (pool.IsFailure)
                    return Result<ResolvedGameMode>.Failure($"Card pool '{poolId}' is invalid: {pool.Error}");
            }
        }
        if (_enemyPools != null)
        {
            foreach (var poolId in mode.Value.EnemyPoolIds)
            {
                var pool = _enemyPools.Get(poolId, configName);
                if (pool.IsFailure)
                    return Result<ResolvedGameMode>.Failure($"Enemy pool '{poolId}' is invalid: {pool.Error}");
                if (pool.Value.EntityDefinitionIds.Count == 0)
                    return Result<ResolvedGameMode>.Failure($"Enemy pool '{poolId}' has no entities");
            }
        }

        return Result<ResolvedGameMode>.Success(new ResolvedGameMode
        {
            Definition = mode.Value,
            FlowRules = flow.Value,
            CombatRules = combat.Value,
            ReplayPolicy = replay.Value,
            TimelinePolicy = timeline.Value,
            ContentBindingPolicy = binding.Value,
            CapabilityPolicy = capabilities.Value
        });
    }

    public Result<ResolvedGameMode> Resolve(string modeId, string configName, string contentRevision)
    {
        if (_contentRuntimes == null)
            return Result<ResolvedGameMode>.Failure("Revisioned game-mode runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<ResolvedGameMode>.Failure(runtime.Error);

        var mode = runtime.Value.GetDefinition<GameModeDefinition>("modes", modeId);
        if (mode.IsFailure)
            return Result<ResolvedGameMode>.Failure(mode.Error);
        if (!string.Equals(mode.Value.ModeId, modeId, StringComparison.Ordinal))
            return Result<ResolvedGameMode>.Failure($"Game mode definition identity mismatch: {modeId}");

        var flow = GetRequired<FlowRulesDefinition>(runtime.Value, mode.Value.FlowRulesId, "flow rules", "flow-rules");
        var combat = GetRequired<CombatRulesDefinition>(runtime.Value, mode.Value.CombatRulesId, "combat rules", "combat-rules");
        var replay = GetRequired<ReplayPolicyDefinition>(runtime.Value, mode.Value.ReplayPolicyId, "replay policy", "replay-policies");
        var timeline = GetRequired<TimelinePolicyDefinition>(runtime.Value, mode.Value.TimelinePolicyId, "timeline policy", "timeline-policies");
        var binding = GetRequired<ContentBindingPolicyDefinition>(
            runtime.Value,
            mode.Value.ContentBindingPolicyId,
            "content binding policy",
            "content-binding-policies");
        var capabilities = GetRequired<CapabilityPolicyDefinition>(
            runtime.Value,
            mode.Value.CapabilityPolicyId,
            "capability policy",
            "capability-policies");

        if (flow.IsFailure || combat.IsFailure || replay.IsFailure || timeline.IsFailure ||
            binding.IsFailure || capabilities.IsFailure)
        {
            var errors = new[]
            {
                flow.IsFailure ? flow.Error : null,
                combat.IsFailure ? combat.Error : null,
                replay.IsFailure ? replay.Error : null,
                timeline.IsFailure ? timeline.Error : null,
                binding.IsFailure ? binding.Error : null,
                capabilities.IsFailure ? capabilities.Error : null
            };
            return Result<ResolvedGameMode>.Failure(string.Join("; ", errors.Where(error => error != null)));
        }

        WarnReservedPolicies(combat.Value);
        var policyValidation = ValidatePolicies(
            combat.Value,
            replay.Value,
            timeline.Value,
            binding.Value,
            capabilities.Value);
        if (policyValidation.IsFailure)
            return Result<ResolvedGameMode>.Failure(policyValidation.Error);

        foreach (var poolId in mode.Value.CardPoolIds)
        {
            var pool = runtime.Value.GetDefinition<CardPoolDefinition>("card-pools", poolId);
            if (pool.IsFailure)
                return Result<ResolvedGameMode>.Failure($"Card pool '{poolId}' is invalid: {pool.Error}");
        }
        foreach (var poolId in mode.Value.EnemyPoolIds)
        {
            var pool = runtime.Value.GetDefinition<EnemyPoolDefinition>("enemy-pools", poolId);
            if (pool.IsFailure)
                return Result<ResolvedGameMode>.Failure($"Enemy pool '{poolId}' is invalid: {pool.Error}");
            if (pool.Value.EntityDefinitionIds.Count == 0)
                return Result<ResolvedGameMode>.Failure($"Enemy pool '{poolId}' has no entities");
        }

        return Result<ResolvedGameMode>.Success(new ResolvedGameMode
        {
            Definition = mode.Value,
            FlowRules = flow.Value,
            CombatRules = combat.Value,
            ReplayPolicy = replay.Value,
            TimelinePolicy = timeline.Value,
            ContentBindingPolicy = binding.Value,
            CapabilityPolicy = capabilities.Value
        });
    }

    private static Result ValidatePolicies(
        CombatRulesDefinition combat,
        ReplayPolicyDefinition replay,
        TimelinePolicyDefinition timeline,
        ContentBindingPolicyDefinition binding,
        CapabilityPolicyDefinition capabilities)
    {
        if (string.IsNullOrWhiteSpace(combat.DefaultPhaseSequenceId))
            return Result.Failure("Combat rules require a phase sequence id");
        var combatFlow = CombatFlowPolicyValidator.Validate(combat.Flow);
        if (combatFlow.IsFailure)
            return combatFlow;
        if (timeline.MaxItemsPerPage is < 1 or > 1000)
            return Result.Failure("Timeline policy maxItemsPerPage must be between 1 and 1000");
        if (capabilities.MaxCards < 0 || capabilities.MaxEnemies < 1 ||
            capabilities.MaxBranchesPerRoot < 0 || capabilities.MaxSimulationCommands < 0)
            return Result.Failure("Capability policy limits are invalid");
        if (replay.AllowForkFromHistory && !timeline.Enabled)
            return Result.Failure("Replay policy requires a timeline when history forks are enabled");
        if (capabilities.AllowTimelineFork && !replay.AllowForkFromHistory)
            return Result.Failure("Capability policy enables timeline forks but replay policy rejects them");
        if (capabilities.AllowHotReloadActivation &&
            !string.Equals(binding.ActiveRuns, "allow_versioned_activation", StringComparison.Ordinal))
        {
            return Result.Failure(
                "Capability policy enables hot reload activation but content binding policy rejects active runs");
        }

        return Result.Success();
    }

    private void WarnReservedPolicies(CombatRulesDefinition combat)
    {
        if (combat.Flow.Reactions.Strategy != ReactionStrategy.Disabled)
        {
            _logger?.LogWarning(
                $"Combat rules '{combat.CombatRulesId}' requested reaction strategy " +
                $"'{combat.Flow.Reactions.Strategy}', but reactions are not implemented");
        }
        if (combat.Flow.EncounterResolution.Strategy != EncounterResolutionStrategy.ManualAck)
        {
            _logger?.LogWarning(
                $"Combat rules '{combat.CombatRulesId}' requested encounter resolution strategy " +
                $"'{combat.Flow.EncounterResolution.Strategy}', but only ManualAck is implemented");
        }
        if (combat.Flow.Outcome.EvaluationBoundary != OutcomeEvaluationBoundary.AfterCurrentAction)
        {
            _logger?.LogWarning(
                $"Combat rules '{combat.CombatRulesId}' requested outcome evaluation boundary " +
                $"'{combat.Flow.Outcome.EvaluationBoundary}', but only AfterCurrentAction is implemented");
        }
    }

    private static Result<T> GetRequired<T>(
        ContentRuntime runtime,
        string? id,
        string label,
        string kind)
    {
        return string.IsNullOrWhiteSpace(id)
            ? Result<T>.Failure($"Game mode requires a {label} id")
            : runtime.GetDefinition<T>(kind, id);
    }

    private static Result<T> GetRequired<T>(
        IResourceCatalog<T> catalog,
        string? id,
        string label,
        string configName)
    {
        return string.IsNullOrWhiteSpace(id)
            ? Result<T>.Failure($"Game mode requires a {label} id")
            : catalog.Get(id, configName);
    }
}
