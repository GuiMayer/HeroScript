using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;

namespace Core.Combat.Intents;

public sealed class IntentResolver : IIntentResolver
{
    private readonly IGambitEngine _gambitEngine;
    private readonly IActionManager _actionManager;

    public IntentResolver(IGambitEngine gambitEngine, IActionManager actionManager)
    {
        _gambitEngine = gambitEngine ?? throw new ArgumentNullException(nameof(gambitEngine));
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
    }

    public Result<CombatIntent> ResolveIntent(CombatState combat, string actorId, Guid? runId = null, IReadOnlyList<string>? gambitIds = null)
    {
        if (combat == null)
            return Result<CombatIntent>.Failure("Combat state is required");
        if (string.IsNullOrWhiteSpace(actorId))
            return Result<CombatIntent>.Failure("Actor id is required");

        var actor = combat.GetEntity(actorId);
        if (actor == null)
            return Result<CombatIntent>.Failure($"Actor not found: {actorId}");

        var decision = _gambitEngine.DecideActionWithMetadata(new Entity.Entity { EntityId = actor.EntityId, DisplayName = actor.Name }, combat, gambitIds);
        if (decision.IsFailure)
            return Result<CombatIntent>.Failure(decision.Error);

        return Result<CombatIntent>.Success(BuildIntent(actor, combat, decision.Value));
    }

    public Result<IReadOnlyList<CombatIntent>> ResolveEnemyIntents(CombatState combat, Guid? runId = null, IReadOnlyList<string>? gambitIds = null)
    {
        if (combat == null)
            return Result<IReadOnlyList<CombatIntent>>.Failure("Combat state is required");

        var intents = new List<CombatIntent>();
        foreach (var enemy in combat.Enemies.Where(enemy => enemy.IsAlive))
        {
            var intent = ResolveIntent(combat, enemy.EntityId, runId, gambitIds);
            if (intent.IsFailure)
                return Result<IReadOnlyList<CombatIntent>>.Failure(intent.Error);

            intents.Add(intent.Value);
        }

        return Result<IReadOnlyList<CombatIntent>>.Success(intents);
    }

    private CombatIntent BuildIntent(CombatEntity actor, CombatState combat, GambitDecision decision)
    {
        var action = decision.Action;
        var actionId = action.PowerId;
        ActionDefinition? actionDefinition = null;
        if (!string.IsNullOrWhiteSpace(actionId))
        {
            var definitionResult = _actionManager.GetDefinition(actionId);
            if (definitionResult.IsSuccess)
                actionDefinition = definitionResult.Value;
        }
        var target = string.IsNullOrWhiteSpace(action.TargetId) ? null : combat.GetEntity(action.TargetId);
        var intentTags = MergeTags(decision.Intent.Tags, actionDefinition?.Tags);

        return new CombatIntent
        {
            ActorId = actor.EntityId,
            ActionType = action.ActionType,
            PowerId = action.PowerId,
            TargetId = action.TargetId,
            CostOptionId = action.CostOptionId?.ToString(),
            DisplayName = ResolveDisplayName(decision, actionDefinition),
            Description = ResolveDescription(decision, actionDefinition),
            TelegraphType = ResolveTelegraphType(decision, actionDefinition),
            EstimatedDamage = EstimateDamage(actionDefinition, actor, target),
            Tags = intentTags,
            Priority = decision.Priority,
            SourceGambitId = decision.GambitId
        };
    }

    private static string ResolveDisplayName(GambitDecision decision, ActionDefinition? actionDefinition)
    {
        if (!string.IsNullOrWhiteSpace(decision.Intent.DisplayName))
            return decision.Intent.DisplayName;
        if (!string.IsNullOrWhiteSpace(actionDefinition?.DisplayName))
            return actionDefinition.DisplayName;

        return decision.Action.ActionType.ToString();
    }

    private static string ResolveDescription(GambitDecision decision, ActionDefinition? actionDefinition)
    {
        if (!string.IsNullOrWhiteSpace(decision.Intent.Description))
            return decision.Intent.Description;

        return actionDefinition?.Description ?? string.Empty;
    }

    private static string ResolveTelegraphType(GambitDecision decision, ActionDefinition? actionDefinition)
    {
        if (!string.IsNullOrWhiteSpace(decision.Intent.TelegraphType))
            return decision.Intent.TelegraphType;
        if (actionDefinition?.Effects.Any(effect => effect.Type == EffectType.DAMAGE) == true)
            return "Attack";
        if (actionDefinition?.Effects.Any(effect => effect.Type == EffectType.HEAL) == true)
            return "Heal";
        if (actionDefinition?.Effects.Any(effect => effect.Type == EffectType.APPLY_STATUS) == true)
            return "Status";

        return decision.Action.ActionType == ActionType.PASS ? "Pass" : "Action";
    }

    private static float? EstimateDamage(ActionDefinition? actionDefinition, CombatEntity actor, CombatEntity? target)
    {
        if (actionDefinition == null || target == null)
            return null;

        var damageEffects = actionDefinition.Effects.Where(effect => effect.Type == EffectType.DAMAGE).ToList();
        if (damageEffects.Count == 0 || damageEffects.Any(effect => !string.IsNullOrWhiteSpace(effect.FormulaValue)))
            return null;

        var total = 0f;
        foreach (var effect in damageEffects)
        {
            if (!effect.FlatValue.HasValue)
                return null;

            total += effect.FlatValue.Value * System.Math.Max(1, effect.Repeat);
        }

        return total;
    }

    private static IReadOnlyList<string> MergeTags(IEnumerable<string>? intentTags, IEnumerable<string>? actionTags)
    {
        return (intentTags ?? Array.Empty<string>())
            .Concat(actionTags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
