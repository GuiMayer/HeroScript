using Core.Common;
using Core.Config;
using Core.Run.Content;

namespace Core.Run;

public interface IGameModeResolver
{
    Result<ResolvedGameMode> Resolve(string modeId, string configName);
}

/// <summary>
/// Resolves the policy graph selected by a mode before a run starts. Keeping
/// this at the engine boundary prevents clients from enabling capabilities by
/// merely sending different JSON fields.
/// </summary>
public sealed class GameModeResolver : IGameModeResolver
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

    public GameModeResolver(
        IResourceCatalog<GameModeDefinition> modes,
        IResourceCatalog<FlowRulesDefinition> flowRules,
        IResourceCatalog<CombatRulesDefinition> combatRules,
        IResourceCatalog<ReplayPolicyDefinition> replayPolicies,
        IResourceCatalog<TimelinePolicyDefinition> timelinePolicies,
        IResourceCatalog<ContentBindingPolicyDefinition> contentBindingPolicies,
        IResourceCatalog<CapabilityPolicyDefinition> capabilityPolicies,
        ICardPoolResolver? cardPools = null,
        IResourceCatalog<EnemyPoolDefinition>? enemyPools = null)
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

        if (timeline.Value.MaxItemsPerPage is < 1 or > 1000)
            return Result<ResolvedGameMode>.Failure("Timeline policy maxItemsPerPage must be between 1 and 1000");
        if (capabilities.Value.MaxCards < 0 || capabilities.Value.MaxEnemies < 1 ||
            capabilities.Value.MaxBranchesPerRoot < 0)
        {
            return Result<ResolvedGameMode>.Failure("Capability policy limits are invalid");
        }
        if (replay.Value.AllowForkFromHistory && !timeline.Value.Enabled)
            return Result<ResolvedGameMode>.Failure("Replay policy requires a timeline when history forks are enabled");
        if (capabilities.Value.AllowTimelineFork && !replay.Value.AllowForkFromHistory)
            return Result<ResolvedGameMode>.Failure("Capability policy enables timeline forks but replay policy rejects them");
        if (capabilities.Value.AllowHotReloadActivation &&
            !string.Equals(binding.Value.ActiveRuns, "allow_versioned_activation", StringComparison.Ordinal))
        {
            return Result<ResolvedGameMode>.Failure(
                "Capability policy enables hot reload activation but content binding policy rejects active runs");
        }

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
