using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Effects;
using Core.Math;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;

namespace Core.Content;

/// <summary>Validates executable components, not merely the JSON shape or existence of their containers.</summary>
internal sealed class GameplayContentValidator(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
{
    public void Validate()
    {
        Visit<FormulaDefinition>("formulas", (path, item) =>
            errors.AddRange(FormulaDefinitionValidator.Validate(item).Select(error => $"{path}: {error}")));
        Visit<GameModeDefinition>("modes", (path, item) =>
        {
            Influences(path, item.Influences);
            var enabled = item.CalculationPipelineIds.ToHashSet(StringComparer.Ordinal);
            foreach (var influence in item.Influences)
            {
                var reachable = enabled.Select(id => runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id))
                    .Any(result => result.IsSuccess && result.Value.Channel == influence.Channel &&
                        result.Value.Buckets.Any(bucket => bucket.BucketId == influence.Bucket));
                if (!reachable) Error($"{path}/influences/{influence.InfluenceId}",
                    "influence is not reachable through a calculation pipeline enabled by this mode");
            }
        });
        Visit<RunDefinition>("runs", (path, item) =>
        {
            var activities = RunActivityRegistry.CreateDefault();
            foreach (var node in item.MapNodes)
            {
                var address = $"{path}/mapNodes/{node.NodeId}";
                var valid = activities.Validate(node.Activity);
                if (valid.IsFailure) Error(address, valid.Error);
                if (!Enum.IsDefined(node.CompletionPolicy)) Error(address, "invalid completionPolicy");
                var handler = activities.Resolve(node.Activity.Type);
                var kind = handler.IsSuccess ? handler.Value.DefinitionKind : null;
                if (kind != null) Reference(address, kind, node.Activity.DefinitionId, required: true);
                if (node.Activity.Type == RunActivityType.Encounter)
                    Encounter(address, node.Activity);
                if (node.Activity.Type == RunActivityType.CardUpgrade)
                    ReferencesParameter(address, node.Activity, "upgradeIds", "card-upgrades", required: true);
                RunBoundaryEffects($"{address}/entryEffects", node.EntryEffects);
                RunBoundaryEffects($"{address}/exitEffects", node.ExitEffects);
            }
        });
        Visit<RunProgressionPolicyDefinition>("run-progression-policies", (path, item) =>
        {
            if (string.IsNullOrWhiteSpace(item.ProgressionPolicyId)) Error(path, "progressionPolicyId is required");
            if (!Enum.IsDefined(item.EncounterVictory) || !Enum.IsDefined(item.EncounterDefeat) ||
                !Enum.IsDefined(item.EncounterDraw) || !Enum.IsDefined(item.EncounterAbandoned) ||
                !Enum.IsDefined(item.EndOfMap))
                Error(path, "invalid lifecycle transition");
            if (!Enum.IsDefined(item.EncounterRetry)) Error(path, "invalid encounterRetry policy");
            if (item.RetryableEncounterOutcomes.Any(outcome =>
                    !Enum.IsDefined(outcome) || outcome == CombatStatus.ACTIVE))
                Error(path, "retryableEncounterOutcomes must contain only terminal combat outcomes");
            if (item.EncounterRetry == RunEncounterRetryPolicy.Disabled && item.RetryableEncounterOutcomes.Count > 0)
                Error(path, "retryableEncounterOutcomes requires encounterRetry RestartActivity");
        });
        Visit<Core.Combat.TurnPhase.PhaseSequenceDefinition>("phase-sequences", (path, item) =>
        {
            foreach (var phase in item.Phases)
            {
                Effects($"{path}/phases/{phase.PhaseId}/entryEffects", phase.EntryEffects);
                Effects($"{path}/phases/{phase.PhaseId}/exitEffects", phase.ExitEffects);
                foreach (var edge in phase.Edges)
                    Formula($"{path}/phases/{phase.PhaseId}/edges/{edge.EdgeId}", edge.Condition);
            }
        });
        Visit<ActionDefinition>("actions", (path, item) => Effects(path, item.Effects));
        Visit<Core.Run.Dialogue.DialogueDefinition>("dialogues", (path, item) =>
        {
            foreach (var error in Core.Run.Dialogue.DialogueDefinitionValidator.Validate(item)) Error(path, error);
            foreach (var node in item.Nodes)
            {
                RunBoundaryEffects($"{path}/{node.NodeId}/entryEffects", node.EntryEffects);
                foreach (var choice in node.Choices)
                {
                    var address = $"{path}/{node.NodeId}/{choice.ChoiceId}";
                    RunBoundaryEffects($"{address}/effects", choice.Effects);
                    foreach (var cost in choice.Costs) Reference(address, "resources", cost.ResourceId, required: true);
                    DialogueConditionReferences(address, choice.Condition);
                }
            }
        });
        Visit<Core.Resources.ResourceDefinition>("resources", (path, item) =>
        {
            if (item.Regeneration is { Enabled: true } regeneration)
                Effects(path, [regeneration.ToEffect(item.ResourceId)]);
        });
        Visit<CardContentDefinition>("cards", (path, item) => Components(path, item.Components));
        Visit<CardComponentBundleDefinition>("card-component-bundles", (path, item) => Components(path, item.Components));
        Visit<StatusEffectDefinition>("status-effects", (path, item) =>
        {
            InstancePolicies(path, item.DefaultStacks, item.MaxStacks, item.DefaultDuration, item.Stacking, item.DurationReapply);
            if (!Enum.IsDefined(item.DurationTickBoundary)) Error(path, "invalid durationTickBoundary");
            Triggers(path, item.Triggers, relic: false);
            Influences(path, item.Influences);
            Unique(path, "constraintId", item.ActionConstraints.Select(item => item.ConstraintId));
            foreach (var constraint in item.ActionConstraints)
                Formula($"{path}/constraints/{constraint.ConstraintId}", constraint.Condition);
        });
        Visit<RelicDefinition>("relics", (path, item) =>
        {
            if (!Enum.IsDefined(item.Stacking) || item.StackLimit < 1) Error(path, "invalid relic stacking policy");
            Triggers(path, item.Triggers, relic: true);
            Influences(path, item.Influences);
        });
        Visit<ScriptModifierDefinition>("modifiers", (path, item) =>
        {
            InstancePolicies(path, item.DefaultStacks, item.MaxStacks, item.DefaultDuration, item.Stacking, item.DurationReapply);
            if (!Enum.IsDefined(item.DurationBoundary)) Error(path, "invalid modifier durationBoundary");
            Influences(path, item.Influences);
        });
    }

    private void DialogueConditionReferences(string path, Core.Run.Dialogue.DialogueCondition? condition)
    {
        if (condition == null) return;
        var kind = condition.Kind switch
        {
            Core.Run.Dialogue.DialogueConditionKind.ResourceAtLeast => "resources",
            Core.Run.Dialogue.DialogueConditionKind.HasCard => "cards",
            Core.Run.Dialogue.DialogueConditionKind.HasRelic => "relics",
            _ => null
        };
        if (kind != null) Reference(path, kind, condition.Id, required: true);
        foreach (var child in condition.Children) DialogueConditionReferences(path, child);
    }

    private void Visit<T>(string kind, Action<string, T> inspect)
    {
        foreach (var id in runtime.GetDefinitions(kind).Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var parsed = runtime.GetDefinition<T>(kind, id);
            if (parsed.IsFailure) { Error($"{kind}/{id}", parsed.Error); continue; }
            try { inspect($"{kind}/{id}", parsed.Value); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
            { Error($"{kind}/{id}", $"invalid component: {exception.Message}"); }
        }
    }

    private void Components(string path, IReadOnlyList<CardComponentDefinition> components)
    {
        foreach (var component in components)
        {
            var address = $"{path}/components/{component.ComponentId}";
            switch (component)
            {
                case CardEffectComponentDefinition effect: Effects(address, [effect.Effect]); break;
                case CardConditionComponentDefinition condition: Formula(address, condition.Expression, required: true); break;
                case CardInfluenceComponentDefinition influence:
                    Influences(address, [new() { InfluenceId = influence.ComponentId, Channel = influence.Channel,
                        Bucket = influence.Bucket, Formula = influence.Formula, Value = influence.Value,
                        RequiredTags = influence.RequiredTags, ExcludedTags = influence.ExcludedTags }]);
                    break;
                case CardTriggerComponentDefinition:
                    Error(address, "card trigger components have no executable lifecycle yet");
                    break;
                case CardTargetingComponentDefinition targeting:
                    if (!Enum.IsDefined(targeting.Target)) Error(address, "invalid targeting policy");
                    Reference(address, "resources", targeting.SelectionResourceId);
                    break;
                case CardDispositionComponentDefinition disposition:
                    if (!Enum.IsDefined(disposition.Destination)) Error(address, "invalid card disposition");
                    break;
            }
        }
    }

    private void Effects(string path, IEnumerable<EffectDefinition> effects)
    {
        errors.AddRange(EffectDefinitionValidator.Validate(effects, (effect, location) =>
        {
            var address = $"{path}/{location}";
            Reference(address, "resources", effect.TargetResource);
            Reference(address, "resources", effect.SelectionResourceId);
            Reference(address, "status-effects", effect.StatusId);
            Reference(address, "modifiers", effect.ModifierId);
            Reference(address, "cards", effect.CardDefinitionId);
            Reference(address, "calculation-pipelines", effect.CalculationPipelineId);
            if (effect.CalculationPipelineId is { Length: > 0 } pipelineId)
            {
                var pipeline = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", pipelineId);
                if (pipeline.IsSuccess && pipeline.Value.Channel != effect.CalculationChannel)
                    Error(address, "explicit calculation pipeline targets a different channel");
            }
            if (effect.Dispel != null)
                foreach (var statusId in effect.Dispel.StatusIds) Reference(address, "status-effects", statusId, required: true);
            Formula(address, effect.FormulaValue);
            Formula(address, effect.Condition);
        }).Select(error => $"{path}/{error}"));
    }

    private void RunBoundaryEffects(string path, IReadOnlyList<EffectDefinition> effects)
    {
        Effects(path, effects);
        foreach (var effect in effects)
        {
            if (effect.Chance != 1)
                Error(path, $"effect '{effect.EffectId}' must have chance 1 at a run boundary");
            if (effect.Target is not (EffectTarget.SELF or EffectTarget.TARGET))
                Error(path, $"effect '{effect.EffectId}' must target the run owner");
            if (effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS)
                Error(path, $"effect '{effect.EffectId}' does not persist in run state");
        }
    }

    private void Encounter(string path, RunActivityDefinition activity)
    {
        if (!activity.Parameters.TryGetValue("participants", out var participants) ||
            participants.ValueKind != System.Text.Json.JsonValueKind.Array ||
            participants.GetArrayLength() < 2)
        {
            Error(path, "encounter parameters require at least two participants");
            return;
        }

        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        var sideIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var participant in participants.EnumerateArray())
        {
            var instanceId = participant.TryGetProperty("instanceId", out var instance) ? instance.GetString() : null;
            var definitionId = participant.TryGetProperty("definitionId", out var definition) ? definition.GetString() : null;
            var sideId = participant.TryGetProperty("sideId", out var side) ? side.GetString() : null;
            if (string.IsNullOrWhiteSpace(instanceId) || !instanceIds.Add(instanceId))
                Error(path, $"encounter participant has a missing or duplicate instanceId '{instanceId}'");
            if (string.IsNullOrWhiteSpace(sideId))
                Error(path, $"encounter participant '{instanceId}' requires sideId");
            else
                sideIds.Add(sideId);
            Reference(path, "entities", definitionId, required: true);
            if (!participant.TryGetProperty("controllerBinding", out var controller) ||
                !controller.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != System.Text.Json.JsonValueKind.String)
                Error(path, $"encounter participant '{instanceId}' requires controllerBinding.kind");
        }
        if (sideIds.Count < 2)
            Error(path, "encounter participants require at least two sides");
    }

    private void ReferencesParameter(
        string path,
        RunActivityDefinition activity,
        string parameter,
        string kind,
        bool required = false)
    {
        if (!activity.Parameters.TryGetValue(parameter, out var values) ||
            values.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            if (required) Error(path, $"activity parameter '{parameter}' is required");
            return;
        }
        foreach (var value in values.EnumerateArray())
            Reference(path, kind, value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null, true);
    }

    private void Triggers(string path, IReadOnlyList<EffectTriggerDefinition> triggers, bool relic)
    {
        Unique(path, "triggerId", triggers.Select(item => item.TriggerId));
        foreach (var trigger in triggers)
        {
            var address = $"{path}/triggers/{trigger.TriggerId}";
            var lifecycle = Enum.TryParse<StatusTriggerBoundary>(trigger.Boundary, false, out var boundary) &&
                Enum.IsDefined(boundary) && boundary != StatusTriggerBoundary.Unspecified && trigger.Boundary == boundary.ToString();
            if (!lifecycle && !(relic && trigger.Boundary is CombatTriggerBoundaries.CombatStart or CombatTriggerBoundaries.CombatEnd))
                Error(address, $"boundary '{trigger.Boundary}' has no executable lifecycle");
            if (trigger.Effects.Count == 0) Error(address, "trigger requires effects");
            Effects(address, trigger.Effects);
        }
    }

    private void Influences(string path, IReadOnlyList<ContextualInfluenceDefinition> influences)
    {
        Unique(path, "influenceId", influences.Select(item => item.InfluenceId));
        foreach (var influence in influences)
        {
            var address = $"{path}/influences/{influence.InfluenceId}";
            var invalid = ContextualInfluencePolicies.Validate(influence);
            if (invalid != null) Error(address, invalid);
            Formula(address, influence.Formula);
            var pipelines = runtime.GetDefinitions("calculation-pipelines").Keys
                .Select(id => runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id))
                .Where(result => result.IsSuccess && result.Value.Channel == influence.Channel).Select(result => result.Value).ToArray();
            if (string.IsNullOrWhiteSpace(influence.Channel) || string.IsNullOrWhiteSpace(influence.Bucket) ||
                !pipelines.Any(pipeline => pipeline.Buckets.Any(bucket => bucket.BucketId == influence.Bucket)))
                Error(address, $"no calculation pipeline contains channel '{influence.Channel}' and bucket '{influence.Bucket}'");
        }
    }

    private void InstancePolicies(string path, int stacks, int maxStacks, int duration, StackReapplyPolicy stacking, DurationReapplyPolicy reapply)
    {
        if (stacks < 1 || maxStacks < stacks) Error(path, "invalid default/max stacks");
        if (duration is 0 or < -1) Error(path, "invalid duration");
        if (!Enum.IsDefined(stacking) || !Enum.IsDefined(reapply)) Error(path, "invalid stacking or duration reapply policy");
    }

    private void Formula(string path, string? expression, bool required = false)
    {
        if (expression == null && !required) return;
        var result = RuntimeFormulaEvaluator.ValidateSyntax(expression ?? string.Empty, IsGameplayVariable,
            id => runtime.GetDefinitions("formulas").ContainsKey(id));
        if (result.IsFailure) Error(path, result.Error);
    }

    private bool IsGameplayVariable(string token)
    {
        if (token is "stacks" or "duration" or "repeat_index" or "target_index") return true;
        if (token is "turn" or "round" or "activation" or "actions_taken" or "phase_order" or
            "command_type" || token.StartsWith("tag_", StringComparison.Ordinal)) return true;
        foreach (var owner in new[] { "source", "target", "owner", "run" })
        {
            var prefix = $"{owner}.resources.";
            if (!token.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var resourceAndField = token[prefix.Length..];
            var separator = resourceAndField.LastIndexOf('.');
            return separator > 0 && runtime.GetDefinitions("resources").ContainsKey(resourceAndField[..separator]) &&
                resourceAndField[(separator + 1)..] is "current" or "maximum" or "minimum" or "percent";
        }
        return false;
    }

    private void Unique(string path, string label, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) Error(path, $"missing or duplicate {label}: '{id}'");
    }

    private void Reference(string path, string kind, string? id, bool required = false)
    {
        if (id == null && !required) return;
        if (string.IsNullOrWhiteSpace(id) || !runtime.GetDefinitions(kind).ContainsKey(id))
            Error(path, $"references missing {kind}/{id}");
    }

    private void Error(string path, string message) => errors.Add($"{path}: {message}");
}
