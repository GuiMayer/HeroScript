using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;

namespace Core.Combat.Flow;

public sealed record CombatResourceLifecycleResult
{
    private ImmutableArray<EffectApplicationRecord> _records = [];

    public CombatState Combat { get; init; } = null!;
    public IReadOnlyList<EffectApplicationRecord> Records
    {
        get => _records;
        init => _records = value?.ToImmutableArray() ?? [];
    }
    public string Fingerprint { get; init; } = string.Empty;
}

public interface ICombatResourceLifecycle
{
    Result<CombatResourceLifecycleResult> Process(
        RunState run,
        CombatState combat,
        string actorId,
        RegenerationTiming timing,
        IReadOnlySet<string>? excludedResourceIds = null);
}

/// <summary>
/// Pure adapter from resource lifecycle definitions to the same immutable,
/// source-agnostic effect processor used by cards, statuses and relics.
/// Publication remains the responsibility of the run's atomic commit.
/// </summary>
public sealed class CombatResourceLifecycle : ICombatResourceLifecycle
{
    private readonly IRuntimeFormulaEvaluator _formulas;
    private readonly IImmutableEffectProcessor _effects;

    public CombatResourceLifecycle(
        IRuntimeFormulaEvaluator formulas,
        IImmutableEffectProcessor effects)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public Result<CombatResourceLifecycleResult> Process(
        RunState run,
        CombatState combat,
        string actorId,
        RegenerationTiming timing,
        IReadOnlySet<string>? excludedResourceIds = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        if (string.IsNullOrWhiteSpace(actorId))
            return Result<CombatResourceLifecycleResult>.Failure("Resource lifecycle actor id is required");
        if (!Enum.IsDefined(timing))
            return Result<CombatResourceLifecycleResult>.Failure($"Unsupported resource lifecycle timing: {timing}");

        var actor = combat.GetEntity(actorId);
        if (actor == null)
            return Result<CombatResourceLifecycleResult>.Failure($"Resource lifecycle actor not found: {actorId}");

        var excluded = excludedResourceIds == null
            ? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase)
            : excludedResourceIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        foreach (var (resourceId, pool) in actor.ResourceState.Resources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var regeneration = pool.Definition?.Regeneration;
            if (excluded.Contains(resourceId) ||
                regeneration is not { Enabled: true } ||
                regeneration.Timing != timing)
            {
                continue;
            }

            var amount = ResolveAmount(run, actor, pool, regeneration);
            if (amount.IsFailure)
                return Result<CombatResourceLifecycleResult>.Failure(amount.Error);
            if (amount.Value == 0f)
                continue;

            commands.Add(new ResolvedEffectCommand
            {
                EffectInstanceId = $"resource-lifecycle:{timing}:{actorId}:{resourceId}",
                SourceEntityId = actorId,
                TargetEntityIds = [actorId],
                ResolvedValue = MathF.Abs(amount.Value),
                Definition = new EffectDefinition
                {
                    EffectId = $"resource-lifecycle:{resourceId}",
                    Type = EffectType.MODIFY_RESOURCE,
                    Target = EffectTarget.SELF,
                    TargetResource = resourceId,
                    Operation = amount.Value > 0f
                        ? ResourceEffectOperation.ADD
                        : ResourceEffectOperation.SUBTRACT
                },
                Provenance = new EffectProvenance
                {
                    Kind = EffectProvenanceKind.Rule,
                    SourceId = $"resource:{resourceId}:regeneration",
                    ComponentId = timing.ToString()
                }
            });
        }

        if (commands.Count == 0)
        {
            return Result<CombatResourceLifecycleResult>.Success(new CombatResourceLifecycleResult
            {
                Combat = combat
            });
        }

        var applied = _effects.Apply(combat, commands.ToImmutable());
        return applied.IsFailure
            ? Result<CombatResourceLifecycleResult>.Failure(applied.Error)
            : Result<CombatResourceLifecycleResult>.Success(new CombatResourceLifecycleResult
            {
                Combat = applied.Value.State,
                Records = applied.Value.Records,
                Fingerprint = applied.Value.Fingerprint
            });
    }

    private Result<float> ResolveAmount(
        RunState run,
        CombatEntity actor,
        ResourcePool pool,
        RegenerationConfig regeneration)
    {
        if (string.IsNullOrWhiteSpace(regeneration.Formula))
            return ValidateFinite(pool.ResourceId, regeneration.AmountPerTurn);

        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["current"] = pool.Current,
            ["min"] = pool.Minimum,
            ["max"] = pool.Maximum,
            ["percent"] = pool.GetPercentage()
        };
        foreach (var (resourceId, resource) in actor.ResourceState.Resources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            variables[$"resource_{resourceId}"] = resource.Current;
            variables[$"resource_{resourceId}_min"] = resource.Minimum;
            variables[$"resource_{resourceId}_max"] = resource.Maximum;
        }

        var evaluated = _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(
                regeneration.Formula,
                run.Determinism.ContentRevision,
                variables,
                regeneration.AmountPerTurn)
            : _formulas.Evaluate(regeneration.Formula, variables, regeneration.AmountPerTurn);
        return evaluated.IsFailure
            ? Result<float>.Failure(
                $"Resource lifecycle formula failed for '{pool.ResourceId}': {evaluated.Error}")
            : ValidateFinite(pool.ResourceId, evaluated.Value);
    }

    private static Result<float> ValidateFinite(string resourceId, float amount) =>
        float.IsNaN(amount) || float.IsInfinity(amount)
            ? Result<float>.Failure($"Resource lifecycle amount must be finite: {resourceId}")
            : Result<float>.Success(amount);
}
