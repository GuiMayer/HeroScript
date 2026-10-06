using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;

namespace Core.Run.Content;

public sealed record CardCompositionPredicate
{
    private ImmutableArray<string> _requiredTags = [], _excludedTags = [], _requiredCapabilities = [], _excludedCapabilities = [];
    public IReadOnlyList<string> RequiredTags { get => _requiredTags; init => _requiredTags = value?.ToImmutableArray() ?? []; }
    public IReadOnlyList<string> ExcludedTags { get => _excludedTags; init => _excludedTags = value?.ToImmutableArray() ?? []; }
    public IReadOnlyList<string> RequiredCapabilities { get => _requiredCapabilities; init => _requiredCapabilities = value?.ToImmutableArray() ?? []; }
    public IReadOnlyList<string> ExcludedCapabilities { get => _excludedCapabilities; init => _excludedCapabilities = value?.ToImmutableArray() ?? []; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardCompositionScope { BeforeSequence, AfterImpact, AfterSequence, BeforeImpact, OncePerProc }

public sealed record CardEffectSelector
{
    private ImmutableArray<EffectType> _effectTypes = [];
    private ImmutableArray<string> _componentIds = [], _requiredEffectTags = [];
    public IReadOnlyList<EffectType> EffectTypes { get => _effectTypes; init => _effectTypes = value?.ToImmutableArray() ?? []; }
    public IReadOnlyList<string> ComponentIds { get => _componentIds; init => _componentIds = value?.ToImmutableArray() ?? []; }
    public IReadOnlyList<string> RequiredEffectTags { get => _requiredEffectTags; init => _requiredEffectTags = value?.ToImmutableArray() ?? []; }
}

public sealed record CardCompositionRuleDefinition
{
    private ImmutableDictionary<string, string> _effectBindings = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
    public string RuleId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string BundleId { get; init; } = string.Empty;
    public CardCompositionScope Scope { get; init; } = CardCompositionScope.AfterImpact;
    public CardCompositionPredicate When { get; init; } = new();
    public CardEffectSelector Selector { get; init; } = new();
    public IReadOnlyDictionary<string, string> EffectBindings
    {
        get => _effectBindings;
        init => _effectBindings = value?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CardCompositionRuleSnapshot(CardCompositionRuleDefinition Rule, CardComponentBundleDefinition Bundle);
public sealed record CardCompositionDiagnostic(string Code, string Message, ulong? TransformationId = null,
    string? RuleId = null, string? Subject = null);
public sealed record CardCompositionApplicationTrace(ulong TransformationId, string RuleId, string BundleId,
    CardCompositionScope Scope, string? AnchorComponentId, ImmutableArray<string> ComponentIds);
public sealed record CardCompositionResult(ImmutableArray<CardComponentDefinition> Components,
    ImmutableArray<CardCompositionApplicationTrace> Trace, ImmutableArray<CardCompositionDiagnostic> Diagnostics);

/// <summary>Bounded lowering to ordinary effect components/children. It never executes gameplay.</summary>
public static class CardCompositionGrammar
{
    public const int MaximumRules = 32;
    public const int MaximumMatches = 256;
    public const int MaximumBundleMembers = 64;

    public static bool ValidSymbols(IReadOnlyList<string> symbols) => symbols.Count <= 64 &&
        symbols.All(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.Length <= 128) &&
        symbols.Distinct(StringComparer.Ordinal).Count() == symbols.Count;

    public static bool IsEmpty(CardCompositionPredicate? predicate) => predicate != null &&
        predicate.RequiredTags.Count + predicate.ExcludedTags.Count + predicate.RequiredCapabilities.Count + predicate.ExcludedCapabilities.Count == 0;

    public static Result ValidatePredicate(CardCompositionPredicate? predicate)
    {
        if (predicate == null || !ValidSymbols(predicate.RequiredTags) || !ValidSymbols(predicate.ExcludedTags) ||
            !ValidSymbols(predicate.RequiredCapabilities) || !ValidSymbols(predicate.ExcludedCapabilities) ||
            predicate.RequiredTags.Intersect(predicate.ExcludedTags, StringComparer.Ordinal).Any() ||
            predicate.RequiredCapabilities.Intersect(predicate.ExcludedCapabilities, StringComparer.Ordinal).Any())
            return Result.Failure("Invalid, duplicate or contradictory composition requirements");
        return Result.Success();
    }

    public static ImmutableArray<string> Capabilities(IEnumerable<CardComponentDefinition> components) => components
        .SelectMany(component => component.CapabilityIds.Concat(component is CardEffectComponentDefinition effect
            ? new[] { $"effect.{effect.Effect.Type}" } : Array.Empty<string>()))
        .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray();

    public static ImmutableArray<CardCompositionDiagnostic> Check(CardCompositionPredicate predicate,
        IReadOnlyList<string> tags, IReadOnlyList<string> capabilities, ulong? transformationId = null, string? ruleId = null)
    {
        var result = ImmutableArray.CreateBuilder<CardCompositionDiagnostic>();
        var valid = ValidatePredicate(predicate);
        if (valid.IsFailure) return [new("invalid_requirements", valid.Error, transformationId, ruleId)];
        void Compare(IReadOnlyList<string> expected, IReadOnlyList<string> actual, bool excluded, string kind)
        {
            foreach (var symbol in expected.OrderBy(id => id, StringComparer.Ordinal))
                if (actual.Contains(symbol, StringComparer.Ordinal) == excluded)
                    result.Add(new(excluded ? $"excluded_{kind}" : $"missing_{kind}",
                        $"Composition {(excluded ? "excludes" : "requires")} {kind}: {symbol}", transformationId, ruleId, symbol));
        }
        Compare(predicate.RequiredTags, tags, false, "tag"); Compare(predicate.ExcludedTags, tags, true, "tag");
        Compare(predicate.RequiredCapabilities, capabilities, false, "capability"); Compare(predicate.ExcludedCapabilities, capabilities, true, "capability");
        return result.ToImmutable();
    }

    public static Result<CardUpgradeDefinition> Seal(CardUpgradeDefinition upgrade, ContentRuntime runtime)
    {
        var requirements = ValidatePredicate(upgrade.Requirements);
        if (requirements.IsFailure) return Result<CardUpgradeDefinition>.Failure(requirements.Error);
        if (upgrade.CompositionRules.Count > MaximumRules || upgrade.CompositionRules.Any(rule => rule == null) ||
            upgrade.CompositionRules.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Count() != upgrade.CompositionRules.Count)
            return Result<CardUpgradeDefinition>.Failure("Composition rules exceed limits or contain duplicate/null identities");
        var snapshots = ImmutableArray.CreateBuilder<CardCompositionRuleSnapshot>();
        foreach (var rule in upgrade.CompositionRules)
        {
            var shape = ValidateRule(rule);
            if (shape.IsFailure) return Result<CardUpgradeDefinition>.Failure($"Rule {rule.RuleId}: {shape.Error}");
            var bundle = runtime.GetDefinition<CardComponentBundleDefinition>("card-component-bundles", rule.BundleId);
            if (bundle.IsFailure) return Result<CardUpgradeDefinition>.Failure(bundle.Error);
            if (bundle.Value.BundleId != rule.BundleId || bundle.Value.Components.Count > MaximumBundleMembers ||
                bundle.Value.Components.Any(component => component is not CardEffectComponentDefinition))
                return Result<CardUpgradeDefinition>.Failure($"Rule {rule.RuleId}: composition bundle must contain bounded effect components only");
            var template = CardBundleCompiler.ValidateTemplate(bundle.Value);
            if (template.IsFailure) return Result<CardUpgradeDefinition>.Failure(template.Error);
            if (!bundle.Value.EffectComponentParameters.Order(StringComparer.Ordinal).SequenceEqual(rule.EffectBindings.Keys.Order(StringComparer.Ordinal)))
                return Result<CardUpgradeDefinition>.Failure($"Rule {rule.RuleId}: bundle effect bindings do not match declared parameters");
            snapshots.Add(new(rule, bundle.Value));
        }
        return Result<CardUpgradeDefinition>.Success(upgrade with { ClosedCompositionRules = snapshots.ToImmutable() });
    }

    private static Result ValidateRule(CardCompositionRuleDefinition rule)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleId) || !CardBundleCompiler.SafeNamespace(rule.Namespace) || rule.Namespace.Length > 47 ||
            string.IsNullOrWhiteSpace(rule.BundleId) || !Enum.IsDefined(rule.Scope) || rule.Selector == null ||
            !ValidSymbols(rule.Selector.ComponentIds) || !ValidSymbols(rule.Selector.RequiredEffectTags) ||
            rule.Selector.EffectTypes.Count > 64 || rule.Selector.EffectTypes.Any(type => !EffectDefinitionValidator.IsExecutable(type)) ||
            rule.Selector.EffectTypes.Distinct().Count() != rule.Selector.EffectTypes.Count ||
            rule.EffectBindings.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            return Result.Failure("Invalid composition rule identity, namespace, scope, selector or binding");
        return ValidatePredicate(rule.When);
    }

    public static CardCompositionResult Compose(IReadOnlyList<string> tags, ImmutableArray<CardComponentDefinition> structural,
        IReadOnlyList<CardUpgradeState> active)
    {
        var diagnostics = ImmutableArray.CreateBuilder<CardCompositionDiagnostic>();
        var trace = ImmutableArray.CreateBuilder<CardCompositionApplicationTrace>();
        var capabilities = Capabilities(structural);
        foreach (var entry in active) diagnostics.AddRange(Check(entry.Requirements, tags, capabilities, entry.TransformationId));
        if (diagnostics.Count > 0) return new(structural, [], diagnostics.ToImmutable());
        var composed = structural.ToList();
        var before = new List<CardComponentDefinition>(); var after = new List<CardComponentDefinition>();
        var memberIds = structural.Select(component => component.ComponentId).ToHashSet(StringComparer.Ordinal);
        static int Nodes(EffectDefinition effect) => 1 + (effect.ChainedEffects ?? []).Sum(Nodes);
        var nodeCount = structural.OfType<CardEffectComponentDefinition>().Sum(component => Nodes(component.Effect)) +
            structural.OfType<CardTriggerComponentDefinition>().SelectMany(component => component.Effects).Sum(Nodes);
        var count = 0;
        foreach (var entry in active)
        {
            if (count > MaximumMatches) break;
            var matchesForUpgrade = 0;
            if (entry.CompositionRules.Count > MaximumRules || entry.CompositionRules.Select(item => item?.Rule?.RuleId).Distinct(StringComparer.Ordinal).Count() != entry.CompositionRules.Count)
            { diagnostics.Add(new("rule_limit", "Composition rules exceed limits or contain duplicate identities", entry.TransformationId)); break; }
            foreach (var snapshot in entry.CompositionRules.OrderBy(item => item?.Rule?.Priority).ThenBy(item => item?.Rule?.RuleId, StringComparer.Ordinal))
            {
                if (count > MaximumMatches) break;
                if (snapshot?.Rule == null || snapshot.Bundle == null) { diagnostics.Add(new("invalid_snapshot", "Invalid composition snapshot", entry.TransformationId)); break; }
                var rule = snapshot.Rule;
                if (snapshot.Bundle.BundleId != rule.BundleId || snapshot.Bundle.Components.Count > MaximumBundleMembers)
                { diagnostics.Add(new("invalid_snapshot", "Composition bundle identity or member limit is invalid", entry.TransformationId, rule.RuleId)); continue; }
                var valid = ValidateRule(rule);
                if (valid.IsFailure) { diagnostics.Add(new("invalid_rule", valid.Error, entry.TransformationId, rule.RuleId)); continue; }
                var template = CardBundleCompiler.ValidateTemplate(snapshot.Bundle);
                if (template.IsFailure || snapshot.Bundle.Components.Any(component => component is not CardEffectComponentDefinition))
                { diagnostics.Add(new("invalid_bundle", template.IsFailure ? template.Error : "Composition bundle must contain effects only", entry.TransformationId, rule.RuleId)); continue; }
                var addedNodes = snapshot.Bundle.Components.Cast<CardEffectComponentDefinition>().Sum(component => Nodes(component.Effect));
                if (!Check(rule.When, tags, capabilities).IsEmpty) continue;
                var anchors = structural.OfType<CardEffectComponentDefinition>().Where(component =>
                    (rule.Selector.EffectTypes.Count == 0 || rule.Selector.EffectTypes.Contains(component.Effect.Type)) &&
                    (rule.Selector.ComponentIds.Count == 0 || rule.Selector.ComponentIds.Contains(component.ComponentId, StringComparer.Ordinal)) &&
                    rule.Selector.RequiredEffectTags.All(tag => component.Effect.Tags.Contains(tag, StringComparer.Ordinal)))
                    .OrderBy(component => component.Order).ThenBy(component => component.ComponentId, StringComparer.Ordinal).ToArray();
                if (anchors.Length == 0) continue;
                var perAnchor = rule.Scope is CardCompositionScope.AfterImpact or CardCompositionScope.BeforeImpact or CardCompositionScope.OncePerProc;
                if (!perAnchor && anchors.Length != 1 && rule.EffectBindings.Values.Contains("$anchor", StringComparer.Ordinal))
                { diagnostics.Add(new("ambiguous_anchor", "Sequence-level bindings require exactly one anchor", entry.TransformationId, rule.RuleId)); continue; }
                var selected = perAnchor ? anchors : anchors.Take(1);
                foreach (var anchor in selected)
                {
                    if (++count > MaximumMatches) { diagnostics.Add(new("match_limit", "Composition match limit exceeded", entry.TransformationId, rule.RuleId)); break; }
                    var scope = rule.Namespace + "_" + CanonicalJson.ComputeHash(anchor.ComponentId)[..16];
                    var bindings = rule.EffectBindings.ToImmutableDictionary(pair => pair.Key,
                        pair => pair.Value == "$anchor" ? anchor.ComponentId : pair.Value, StringComparer.Ordinal);
                    // Count only after template validation; malformed trees cannot bypass the common traversal limits.
                    if (nodeCount + addedNodes > EffectExecutionLimits.MaximumSteps)
                        return new(structural, [], [new("expansion_limit", "Composition exceeds the common effect expansion budget", entry.TransformationId, rule.RuleId)]);
                    var expanded = CardBundleCompiler.Expand(snapshot.Bundle, scope, bindings);
                    if (expanded.IsFailure) { diagnostics.Add(new("invalid_bundle", expanded.Error, entry.TransformationId, rule.RuleId)); continue; }
                    if (expanded.Value.Any(component => component is not CardEffectComponentDefinition))
                    { diagnostics.Add(new("invalid_bundle", "Composition bundle must contain effects only", entry.TransformationId, rule.RuleId)); continue; }
                    var members = expanded.Value.OrderBy(component => component.Order).ThenBy(component => component.ComponentId, StringComparer.Ordinal).ToImmutableArray();
                    var collision = members.FirstOrDefault(component => memberIds.Contains(component.ComponentId));
                    if (collision != null)
                    { diagnostics.Add(new("component_collision", "Composition component ID collision", entry.TransformationId, rule.RuleId, collision.ComponentId)); continue; }
                    foreach (var member in members) memberIds.Add(member.ComponentId);
                    nodeCount += addedNodes;
                    if (perAnchor)
                    {
                        var index = composed.FindIndex(component => component.ComponentId == anchor.ComponentId);
                        var current = (CardEffectComponentDefinition)composed[index];
                        composed[index] = current with { Effect = current.Effect with
                            { ChainedEffects = (current.Effect.ChainedEffects ?? []).Concat(members.Cast<CardEffectComponentDefinition>().Select(component => component.Effect with
                                { ChildTiming = rule.Scope == CardCompositionScope.BeforeImpact ? EffectChildTiming.BeforeParentImpact : EffectChildTiming.AfterParentImpact,
                                  ExecutionScope = rule.Scope == CardCompositionScope.OncePerProc ? EffectExecutionScope.OncePerParentProc : component.Effect.ExecutionScope })).ToImmutableArray() } };
                    }
                    else (rule.Scope == CardCompositionScope.BeforeSequence ? before : after).AddRange(members);
                    trace.Add(new(entry.TransformationId, rule.RuleId, snapshot.Bundle.BundleId, rule.Scope,
                        anchor.ComponentId, members.Select(component => component.ComponentId).ToImmutableArray()));
                    matchesForUpgrade++;
                }
            }
            if (entry.CompositionRules.Count > 0 && matchesForUpgrade == 0 && diagnostics.Count == 0)
                diagnostics.Add(new("no_matching_rule", "No composition rule matches the permanent card", entry.TransformationId));
        }
        try
        {
            var minimum = structural.Length == 0 ? 0 : structural.Min(component => component.Order);
            var maximum = structural.Length == 0 ? 0 : structural.Max(component => component.Order);
            for (var index = 0; index < before.Count; index++) composed.Add(before[index] with { Order = checked(minimum - before.Count + index) });
            for (var index = 0; index < after.Count; index++) composed.Add(after[index] with { Order = checked(maximum + 1 + index) });
        }
        catch (OverflowException) { diagnostics.Add(new("order_overflow", "Composition order exceeds integer bounds")); }
        return new(composed.ToImmutableArray(), trace.ToImmutable(), diagnostics.ToImmutable());
    }
}
