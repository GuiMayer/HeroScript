using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Resources;
using System.Text.Json;

namespace Core.Run.Content;

public sealed record CardTransformationOption(Guid CardInstanceId, string CardDefinitionId,
    CardTransformationOperation Operation, ulong? TargetTransformationId, string? UpgradeId,
    CardTransformationCategory Category, string? SlotId)
{
    public ImmutableArray<ResourceAmount> Costs { get; init; } = [];
}

public sealed record CardTransformationAssessment(Guid RunId, Guid CardInstanceId, string ContentRevision,
    int ExpectedSequence, ulong ExpectedStep, bool IsCompatible, ImmutableArray<CardCompositionDiagnostic> Diagnostics,
    ImmutableArray<string> ChangedComponentIds, ImmutableArray<CardCompositionApplicationTrace> CompositionTrace)
{
    public EffectiveCardDefinition? Before { get; init; }
    public EffectiveCardDefinition? After { get; init; }
    public ImmutableArray<ResourceAmount> Costs { get; init; } = [];
}

public static class CardTransformationAccess
{
    public static Result<ImmutableArray<ResourceAmount>> ReadCosts(RunActivityDefinition activity)
    {
        if (!activity.Parameters.TryGetValue("costs", out var value)) return Result<ImmutableArray<ResourceAmount>>.Success([]);
        try
        {
            var costs = value.Deserialize<ImmutableArray<ResourceAmount>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (costs.IsDefault || costs.Any(cost => cost == null || string.IsNullOrWhiteSpace(cost.ResourceId) || !float.IsFinite(cost.Amount) || cost.Amount < 0) ||
                costs.Select(cost => cost.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != costs.Length)
                return Result<ImmutableArray<ResourceAmount>>.Failure("Invalid transformation activity costs");
            return Result<ImmutableArray<ResourceAmount>>.Success(costs);
        }
        catch (JsonException) { return Result<ImmutableArray<ResourceAmount>>.Failure("Invalid transformation activity costs"); }
    }

    public static Result<ResourceSet> SpendCosts(RunState run)
    {
        var node = run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId);
        if (node?.Activity.Type != RunActivityType.CardUpgrade) return Result<ResourceSet>.Success(run.ResourceState);
        var costs = ReadCosts(node.Activity);
        if (costs.IsFailure) return Result<ResourceSet>.Failure(costs.Error);
        var spent = RunResourceTransitions.Spend(run.ResourceState, costs.Value, $"transform:{node.NodeId}:{run.Determinism.Step}");
        return spent.IsFailure ? Result<ResourceSet>.Failure(spent.Error) : Result<ResourceSet>.Success(spent.Value.State);
    }
    public static bool IsCommand(string type) => type is RunCommandTypes.UpgradeCard or
        RunCommandTypes.RemoveCardTransformation or RunCommandTypes.ReplaceCardTransformation;

    public static string CommandType(CardTransformationOperation operation) => operation switch
    {
        CardTransformationOperation.Apply => RunCommandTypes.UpgradeCard,
        CardTransformationOperation.Remove => RunCommandTypes.RemoveCardTransformation,
        CardTransformationOperation.Replace => RunCommandTypes.ReplaceCardTransformation,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public static Result Validate(RunState run, CardTransformationOperation operation, string? upgradeId)
    {
        if (run.Lifecycle != RunLifecycleState.Active) return Result.Failure("Run is not active");
        if (run.ActiveEncounterId != null) return Result.Failure("Permanent card transformations require leaving the active encounter");
        if (!Enum.IsDefined(operation)) return Result.Failure("Invalid transformation operation");
        if (run.ResolvedMode?.ProgressionPolicy.AllowOutOfActivityCommands == true) return Result.Success();
        var node = run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId);
        if (node?.Activity.Type != RunActivityType.CardUpgrade ||
            run.CompletedActivityNodeIds.Contains(node.NodeId, StringComparer.Ordinal) ||
            run.Map.ResolvedNodeIds.Contains(node.NodeId, StringComparer.Ordinal))
            return Result.Failure("Current activity does not allow card transformations");
        if (operation != CardTransformationOperation.Apply && !Enabled(node,
                operation == CardTransformationOperation.Remove ? "allowRemoval" : "allowReplacement"))
            return Result.Failure("Current activity does not allow this transformation operation");
        if (operation != CardTransformationOperation.Remove && !UpgradeIds(node).Contains(upgradeId, StringComparer.Ordinal))
            return Result.Failure("Upgrade is not offered by the current activity");
        return Result.Success();
    }

    public static bool Enabled(RunMapNodeState node, string key) =>
        node.Activity.Parameters.TryGetValue(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;

    public static IReadOnlyList<string> UpgradeIds(RunMapNodeState node) =>
        node.Activity.Parameters.TryGetValue("upgradeIds", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetString()!).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()
            : [];
}

/// <summary>One pinned, pure authority for commands and executable option discovery.</summary>
public sealed class CardTransformationPlanner(ContentRuntime runtime)
{
    // Definition-only memoization belongs to this pinned planner, never to card identity.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Result<CompiledCardDefinition>> _cards = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Result<CardUpgradeDefinition>> _upgrades = new(StringComparer.Ordinal);

    private Result<CardUpgradeDefinition> Upgrade(string id) => _upgrades.GetOrAdd(id, key =>
    {
        var authored = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", key);
        if (authored.IsFailure) return authored;
        if (authored.Value.UpgradeId != key || authored.Value.Patches.Count == 0 && authored.Value.CompositionRules.Count == 0)
            return Result<CardUpgradeDefinition>.Failure("Invalid upgrade identity or empty patches");
        return CardBundleCompiler.Seal(authored.Value, runtime);
    });

    public Result<CardInstanceState> Plan(RunState run, Guid cardInstanceId,
        CardTransformationOperation operation, ulong? targetTransformationId = null, string? upgradeId = null)
        => PlanCore(run, cardInstanceId, operation, targetTransformationId, upgradeId, null);

    private Result<CardInstanceState> PlanCore(RunState run, Guid cardInstanceId, CardTransformationOperation operation,
        ulong? targetTransformationId, string? upgradeId, ICollection<CardCompositionDiagnostic>? diagnostics)
    {
        var access = CardTransformationAccess.Validate(run, operation, upgradeId);
        if (access.IsFailure) return Result<CardInstanceState>.Failure(access.Error);
        var affordability = CardTransformationAccess.SpendCosts(run);
        if (affordability.IsFailure) return Result<CardInstanceState>.Failure(affordability.Error);
        if (runtime.Manifest.Revision != run.Determinism.ContentRevision ||
            !string.Equals(runtime.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result<CardInstanceState>.Failure("Transformation runtime does not belong to this run revision/configuration");
        if (!run.Deck.Topology.Instances.TryGetValue(cardInstanceId, out var card))
            return Result<CardInstanceState>.Failure($"Card instance not found: {cardInstanceId}");
        if (operation == CardTransformationOperation.Apply ? targetTransformationId != null : targetTransformationId is null or 0)
            return Result<CardInstanceState>.Failure("Invalid selected transformation identity");
        CardUpgradeDefinition? definition = null;
        if (operation != CardTransformationOperation.Remove)
        {
            if (string.IsNullOrWhiteSpace(upgradeId)) return Result<CardInstanceState>.Failure("UpgradeId is required");
            var closed = Upgrade(upgradeId);
            if (closed.IsFailure) return Result<CardInstanceState>.Failure(closed.Error);
            definition = closed.Value;
        }
        else if (upgradeId != null) return Result<CardInstanceState>.Failure("Removal cannot contain an upgradeId");
        var transition = operation switch
        {
            CardTransformationOperation.Apply => CardInstanceUpgradeTransitions.Apply(card, definition!, runtime.Manifest.Revision),
            CardTransformationOperation.Remove => CardInstanceUpgradeTransitions.Remove(card, targetTransformationId!.Value, runtime.Manifest.Revision),
            CardTransformationOperation.Replace => CardInstanceUpgradeTransitions.Replace(card, targetTransformationId!.Value, definition!, runtime.Manifest.Revision),
            _ => Result<CardInstanceState>.Failure("Invalid transformation operation")
        };
        if (transition.IsFailure) return transition;
        var compiled = _cards.GetOrAdd(card.DefinitionId, id => new CardContentCompiler().Compile(id, runtime));
        if (compiled.IsFailure) return Result<CardInstanceState>.Failure(compiled.Error);
        var effective = new EffectiveCardResolver().Resolve(compiled.Value, transition.Value, diagnostics);
        if (effective.IsFailure) return Result<CardInstanceState>.Failure(effective.Error);
        var references = GameplayContentValidator.ValidateCardContainer(runtime,
            $"card-instances/{card.CardInstanceId}", effective.Value.Components);
        return references.IsFailure ? Result<CardInstanceState>.Failure(references.Error) : transition;
    }

    /// <summary>Read-only assessment using exactly the same planner as executable discovery/commands.</summary>
    public CardTransformationAssessment Assess(RunState run, Guid cardInstanceId, CardTransformationOperation operation,
        ulong? targetTransformationId = null, string? upgradeId = null)
    {
        var diagnostics = new List<CardCompositionDiagnostic>();
        var planned = PlanCore(run, cardInstanceId, operation, targetTransformationId, upgradeId, diagnostics);
        if (planned.IsFailure)
        {
            if (diagnostics.Count == 0) diagnostics.Add(new("transformation_invalid", planned.Error));
            return new(run.RunId, cardInstanceId, run.Determinism.ContentRevision, run.Sequence, run.Determinism.Step,
                false, diagnostics.ToImmutableArray(), [], []);
        }
        var compiled = _cards[planned.Value.DefinitionId].Value;
        var resolver = new EffectiveCardResolver();
        var before = resolver.Resolve(compiled, run.Deck.Topology.Instances[cardInstanceId]);
        var after = resolver.Resolve(compiled, planned.Value).Value;
        if (before.IsFailure)
            return new(run.RunId, cardInstanceId, run.Determinism.ContentRevision, run.Sequence, run.Determinism.Step,
                false, [new("invalid_current_composition", before.Error)], [], []);
        var changed = before.Value.Components.Concat(after.Components).Select(component => component.ComponentId)
            .Distinct(StringComparer.Ordinal).Where(id =>
            {
                var oldComponent = before.Value.Components.FirstOrDefault(component => component.ComponentId == id);
                var newComponent = after.Components.FirstOrDefault(component => component.ComponentId == id);
                return oldComponent == null || newComponent == null ||
                    CanonicalJson.ComputeHash<CardComponentDefinition>(oldComponent) != CanonicalJson.ComputeHash<CardComponentDefinition>(newComponent);
            }).Concat(after.CompositionTrace.SelectMany(item => item.ComponentIds)
                .Except(before.Value.CompositionTrace.SelectMany(item => item.ComponentIds), StringComparer.Ordinal))
            .Concat(before.Value.CompositionTrace.SelectMany(item => item.ComponentIds)
                .Except(after.CompositionTrace.SelectMany(item => item.ComponentIds), StringComparer.Ordinal))
            .Concat(before.Value.CompositionTrace.Concat(after.CompositionTrace).Select(item => item.AnchorComponentId)
                .OfType<string>().Distinct(StringComparer.Ordinal).Where(id =>
                    CanonicalJson.ComputeHash(before.Value.CompositionTrace.Where(item => item.AnchorComponentId == id).ToArray()) !=
                    CanonicalJson.ComputeHash(after.CompositionTrace.Where(item => item.AnchorComponentId == id).ToArray())))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray();
        return new(run.RunId, cardInstanceId, run.Determinism.ContentRevision, run.Sequence, run.Determinism.Step,
            true, [], changed, after.CompositionTrace) { Before = before.Value, After = after,
            Costs = run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId)?.Activity is { Type: RunActivityType.CardUpgrade } activity
                ? CardTransformationAccess.ReadCosts(activity).Value : [] };
    }

    public Result<IReadOnlyList<CardTransformationOption>> Options(RunState run, Guid? cardInstanceId = null)
    {
        if (cardInstanceId != null && !run.Deck.Topology.Instances.ContainsKey(cardInstanceId.Value))
            return Result<IReadOnlyList<CardTransformationOption>>.Failure($"Card instance not found: {cardInstanceId}");
        // A missing/cross-setting revision is an error, not an empty catalog fallback.
        if (runtime.Manifest.Revision != run.Determinism.ContentRevision ||
            !string.Equals(runtime.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result<IReadOnlyList<CardTransformationOption>>.Failure("Transformation runtime does not belong to this run revision/configuration");
        var definitions = new List<CardUpgradeDefinition>();
        foreach (var id in runtime.GetDefinitions("card-upgrades").Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", id);
            if (definition.IsFailure) return Result<IReadOnlyList<CardTransformationOption>>.Failure(definition.Error);
            definitions.Add(definition.Value);
        }
        var options = ImmutableArray.CreateBuilder<CardTransformationOption>();
        foreach (var card in run.Deck.Topology.Instances.Values.Where(card => cardInstanceId == null || card.CardInstanceId == cardInstanceId)
            .OrderBy(card => card.CreationOrdinal).ThenBy(card => card.CardInstanceId))
        {
            var active = CardTransformationLedger.Project(card.Upgrades);
            if (active.IsFailure) return Result<IReadOnlyList<CardTransformationOption>>.Failure(active.Error);
            void Offer(CardTransformationOperation operation, ulong? target, CardUpgradeDefinition? definition)
            {
                var planned = Plan(run, card.CardInstanceId, operation, target, definition?.UpgradeId);
                if (planned.IsFailure) return;
                var entry = planned.Value.Upgrades.Last();
                options.Add(new(card.CardInstanceId, card.DefinitionId, operation, target,
                    definition?.UpgradeId, entry.Category, entry.SlotId) { Costs =
                    run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId)?.Activity is { Type: RunActivityType.CardUpgrade } activity
                        ? CardTransformationAccess.ReadCosts(activity).Value : [] });
            }
            foreach (var definition in definitions) Offer(CardTransformationOperation.Apply, null, definition);
            foreach (var entry in active.Value)
            {
                Offer(CardTransformationOperation.Remove, entry.TransformationId, null);
                foreach (var definition in definitions) Offer(CardTransformationOperation.Replace, entry.TransformationId, definition);
            }
        }
        return Result<IReadOnlyList<CardTransformationOption>>.Success(options.ToImmutable());
    }
}
