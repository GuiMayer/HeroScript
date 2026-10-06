using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Run;

public sealed partial class CardTransformationCompositionTests
{
    private static CardCompositionRuleDefinition Rule(string id = "residual", CardCompositionScope scope = CardCompositionScope.AfterImpact) => new()
    {
        RuleId = id, Namespace = id, BundleId = "pulse", Scope = scope,
        Selector = new() { EffectTypes = [EffectType.DAMAGE] }
    };
    private static CardUpgradeDefinition GrammarUpgrade(params CardCompositionRuleDefinition[] rules) => new()
        { UpgradeId = "grammar", Category = CardTransformationCategory.Affinity, CompositionRules = rules };
    private static CardInstanceState ApplyGrammar(ContentRuntime runtime, string id = "grammar")
    {
        var applied = new CardTransformationPlanner(runtime).Plan(State(), CardId, CardTransformationOperation.Apply, upgradeId: id);
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        return applied.Value;
    }

    [Theory]
    [InlineData(CardCompositionScope.BeforeSequence)]
    [InlineData(CardCompositionScope.AfterImpact)]
    [InlineData(CardCompositionScope.AfterSequence)]
    [InlineData(CardCompositionScope.BeforeImpact)]
    [InlineData(CardCompositionScope.OncePerProc)]
    public void Grammar_LowersOnlyToCommonEffectsAtDeclaredBoundary(CardCompositionScope scope)
    {
        var runtime = Runtime([GrammarUpgrade(Rule(scope: scope))], Bundle(1));
        var effective = Resolve(ApplyGrammar(runtime), runtime);
        var roots = effective.All<CardEffectComponentDefinition>();
        var childScope = scope is CardCompositionScope.AfterImpact or CardCompositionScope.BeforeImpact or CardCompositionScope.OncePerProc;
        Assert.Equal(childScope ? 1 : 2, roots.Count);
        Assert.Equal(scope, Assert.Single(effective.CompositionTrace).Scope);
        if (childScope) Assert.Single(roots[0].Effect.ChainedEffects!);
        else Assert.Equal(scope == CardCompositionScope.BeforeSequence ? 1 : 6, roots[0].Effect.FlatValue);
        Assert.Contains("effect.DAMAGE", effective.Capabilities);
    }

    [Theory]
    [InlineData((CardCompositionScope)99)]
    public void Grammar_RejectsUnknownScopes(CardCompositionScope scope)
    {
        var upgrade = GrammarUpgrade(Rule(scope: scope));
        Assert.True(CardBundleCompiler.Seal(upgrade, Runtime([upgrade], Bundle(1))).IsFailure);
    }

    [Fact]
    public void Grammar_UsesArbitraryTagsAndActions_NotElementNamesOrImplicitCosts()
    {
        foreach (var type in new[] { EffectType.DAMAGE, EffectType.HEAL })
        {
            var upgrade = GrammarUpgrade(Rule("hurt") with { When = new() { RequiredTags = ["violet_17"] } },
                Rule("restore") with { Selector = new() { EffectTypes = [EffectType.HEAL] }, When = new() { RequiredTags = ["violet_17"] } })
                with { Patches = [new CardTagsPatchDefinition { Add = ["violet_17"] }] };
            var cost = new CardCostComponentDefinition { ComponentId = "price", Costs = new() { Costs = [new() { ResourceId = "health", Amount = 2 }] } };
            var card = Base() with { Components = [Impact(6) with { Effect = Impact(6).Effect with { Type = type } }, cost] };
            var runtime = Runtime([upgrade], Bundle(1), card);
            var effective = Resolve(ApplyGrammar(runtime), runtime);
            Assert.Equal(type == EffectType.DAMAGE ? "hurt" : "restore", Assert.Single(effective.CompositionTrace).RuleId);
            Assert.Equal(CanonicalJson.ComputeHash<CardComponentDefinition>(cost),
                CanonicalJson.ComputeHash<CardComponentDefinition>(effective.SingleOrDefault<CardCostComponentDefinition>()!));
        }
    }

    [Theory]
    [InlineData("missing_tag")]
    [InlineData("excluded_tag")]
    [InlineData("missing_capability")]
    [InlineData("excluded_capability")]
    public void Grammar_AssessmentReturnsStructuredIncompatibilityWithoutMutation(string code)
    {
        var predicate = code switch
        {
            "missing_tag" => new CardCompositionPredicate { RequiredTags = ["missing"] },
            "excluded_tag" => new CardCompositionPredicate { ExcludedTags = ["attack"] },
            "missing_capability" => new CardCompositionPredicate { RequiredCapabilities = ["missing"] },
            _ => new CardCompositionPredicate { ExcludedCapabilities = ["effect.DAMAGE"] }
        };
        var runtime = Runtime([GrammarUpgrade(Rule()) with { Requirements = predicate }], Bundle(1));
        var run = State(); var hash = CanonicalJson.ComputeHash(run);
        var assessment = new CardTransformationPlanner(runtime).Assess(run, CardId, CardTransformationOperation.Apply, upgradeId: "grammar");
        Assert.False(assessment.IsCompatible);
        var diagnostic = Assert.Single(assessment.Diagnostics);
        Assert.Equal(code, diagnostic.Code); Assert.NotNull(diagnostic.Subject); Assert.Equal(1UL, diagnostic.TransformationId);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run)); Assert.Empty(assessment.ChangedComponentIds);
    }

    [Fact]
    public void Grammar_RequirementsUseFinalStructuralComposition_AndPreventDependentRemoval()
    {
        var capability = new CardUpgradeDefinition { UpgradeId = "enable", Patches = [new CardComponentPatchDefinition
            { ComponentId = "impact", Operation = CardComponentPatchOperation.Replace, Component = Impact(6) with { CapabilityIds = ["custom.ready"] } }] };
        var upgrade = GrammarUpgrade(Rule()) with { Requirements = new() { RequiredCapabilities = ["custom.ready"] } };
        var runtime = Runtime([capability, upgrade], Bundle(1)); var planner = new CardTransformationPlanner(runtime);
        Assert.False(planner.Assess(State(), CardId, CardTransformationOperation.Apply, upgradeId: "grammar").IsCompatible);
        var run = WithCard(State(), ApplyGrammar(runtime, "enable"));
        var next = planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "grammar");
        Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
        run = WithCard(run, next.Value);
        Assert.False(planner.Assess(run, CardId, CardTransformationOperation.Remove, 1).IsCompatible);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Remove, 2).IsSuccess);
    }

    [Fact]
    public void Grammar_ReplacementAndRemovalEraseOldContributions()
    {
        var first = GrammarUpgrade(Rule("first"));
        var second = GrammarUpgrade(Rule("second")) with { UpgradeId = "replacement" };
        var runtime = Runtime([first, second], Bundle(1)); var planner = new CardTransformationPlanner(runtime);
        var run = WithCard(State(), ApplyGrammar(runtime));
        var replaced = planner.Plan(run, CardId, CardTransformationOperation.Replace, 1, "replacement").Value;
        Assert.Equal("second", Assert.Single(Resolve(replaced, runtime).CompositionTrace).RuleId);
        var assessment = planner.Assess(run, CardId, CardTransformationOperation.Replace, 1, "replacement");
        Assert.Contains("impact", assessment.ChangedComponentIds);
        Assert.Equal(3, assessment.ChangedComponentIds.Length);
        var removed = planner.Plan(WithCard(run, replaced), CardId, CardTransformationOperation.Remove, 2).Value;
        var effective = Resolve(removed, runtime);
        Assert.Empty(effective.CompositionTrace); Assert.Empty(effective.All<CardEffectComponentDefinition>()[0].Effect.ChainedEffects ?? []);
    }

    [Fact]
    public void Grammar_ExplicitPriorityThenIdentityIsDeterministic()
    {
        var upgrade = GrammarUpgrade(Rule("z") with { Priority = 10 }, Rule("b"), Rule("a"));
        var runtime = Runtime([upgrade], Bundle(1));
        Assert.Equal(new[] { "a", "b", "z" }, Resolve(ApplyGrammar(runtime), runtime).CompositionTrace.Select(item => item.RuleId));
        var reversed = Runtime([upgrade with { CompositionRules = upgrade.CompositionRules.Reverse().ToArray() }], Bundle(1));
        var first = Resolve(ApplyGrammar(runtime), runtime);
        var second = Resolve(ApplyGrammar(reversed), reversed);
        Assert.Equal(CanonicalJson.ComputeHash(first.Components), CanonicalJson.ComputeHash(second.Components));
    }

    [Fact]
    public void Grammar_DoesNotRecursivelyMatchItsOwnEmittedCapabilities()
    {
        var upgrade = GrammarUpgrade(Rule("first"), Rule("recursive") with { When = new() { RequiredCapabilities = ["emitted.only"] } });
        var runtime = Runtime([upgrade], Bundle(1) with { Components = [Impact(1) with { CapabilityIds = ["emitted.only"] }] });
        Assert.Equal("first", Assert.Single(Resolve(ApplyGrammar(runtime), runtime).CompositionTrace).RuleId);
    }

    [Fact]
    public void Grammar_ParameterizedResidualBindsUpgradedPermanentBase_NotContextualBuffs()
    {
        var bundle = new CardComponentBundleDefinition
        {
            BundleId = "pulse", EffectComponentParameters = ["base"], Components =
            [new CardEffectComponentDefinition { ComponentId = "residual", Effect = new() { Type = EffectType.APPLY_STATUS, StatusId = "unknown",
                PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "$base" }] } }]
        };
        var rule = Rule() with { EffectBindings = new Dictionary<string, string> { ["base"] = "$anchor" } };
        var runtime = Runtime([GrammarUpgrade(rule), Numeric("plus", 4)], bundle);
        var sealedDefinition = CardBundleCompiler.Seal(GrammarUpgrade(rule), runtime).Value;
        var card = CardInstanceUpgradeTransitions.Apply(State().Deck.Topology.Instances[CardId], sealedDefinition, Revision).Value;
        card = CardInstanceUpgradeTransitions.Apply(card, Numeric("plus", 4), Revision).Value;
        var effective = Resolve(card, runtime);
        var effect = effective.All<CardEffectComponentDefinition>()[0].Effect;
        Assert.Equal(10, effect.FlatValue);
        Assert.Equal("impact", Assert.Single(Assert.Single(effect.ChainedEffects!).PayloadBindings).CardEffectComponentId);
        Assert.True(CardBundleCompiler.ValidateTemplate(bundle).IsSuccess);
        Assert.True(CardBundleCompiler.Expand(bundle, "plain").IsFailure);
        Assert.Equal("$base", Assert.IsType<CardEffectComponentDefinition>(bundle.Components[0]).Effect.PayloadBindings[0].CardEffectComponentId);
    }

    [Fact]
    public void Grammar_RejectsUndeclaredOrMissingParameterBindings()
    {
        var bundle = Bundle(1) with { EffectComponentParameters = ["base"] };
        var upgrade = GrammarUpgrade(Rule());
        Assert.True(CardBundleCompiler.Seal(upgrade, Runtime([upgrade], bundle)).IsFailure);
        Assert.True(CardBundleCompiler.ValidateTemplate(bundle with { EffectComponentParameters = ["bad.param"] }).IsFailure);
        Assert.True(CardBundleCompiler.Expand(Bundle(1), "plain", new Dictionary<string, string> { ["extra"] = "impact" }).IsFailure);
    }

    [Fact]
    public void Grammar_RejectsAmbiguousSequenceBindingAndStaleExplicitBase()
    {
        var bundle = Bundle(1) with { EffectComponentParameters = ["base"] };
        var rule = Rule(scope: CardCompositionScope.AfterSequence) with { EffectBindings = new Dictionary<string, string> { ["base"] = "$anchor" } };
        var runtime = Runtime([GrammarUpgrade(rule)], bundle, Base() with { Components = [Impact(6), Impact(7) with { ComponentId = "other" }] });
        Assert.Equal("ambiguous_anchor", Assert.Single(new CardTransformationPlanner(runtime).Assess(State(), CardId, CardTransformationOperation.Apply, upgradeId: "grammar").Diagnostics).Code);
        bundle = bundle with { Components = [new CardEffectComponentDefinition { ComponentId = "residual", Effect = new()
            { Type = EffectType.APPLY_STATUS, StatusId = "burn", PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "$base" }] } }] };
        runtime = Runtime([GrammarUpgrade(Rule() with { EffectBindings = new Dictionary<string, string> { ["base"] = "missing" } })], bundle);
        Assert.Equal("invalid_composition", Assert.Single(new CardTransformationPlanner(runtime).Assess(State(), CardId, CardTransformationOperation.Apply, upgradeId: "grammar").Diagnostics).Code);
    }

    [Fact]
    public void Grammar_CompatiblePairsWorkButNamespaceCollisionsAreRejected()
    {
        var first = GrammarUpgrade(Rule());
        var second = GrammarUpgrade(Rule("other")) with { UpgradeId = "other" };
        var runtime = Runtime([first, second], Bundle(1));
        var run = WithCard(State(), ApplyGrammar(runtime)); var planner = new CardTransformationPlanner(runtime);
        Assert.True(planner.Assess(run, CardId, CardTransformationOperation.Apply, upgradeId: "other").IsCompatible);
        runtime = Runtime([first, second with { CompositionRules = [Rule("other") with { Namespace = "residual" }] }], Bundle(1));
        Assert.Equal("component_collision", Assert.Single(new CardTransformationPlanner(runtime).Assess(run, CardId,
            CardTransformationOperation.Apply, upgradeId: "other").Diagnostics).Code);
    }

    [Fact]
    public void Grammar_NoMatchingRuleIsAnExplicitIncompatibility()
    {
        var runtime = Runtime([GrammarUpgrade(Rule() with { Selector = new() { EffectTypes = [EffectType.HEAL] } })], Bundle(1));
        Assert.Equal("no_matching_rule", Assert.Single(new CardTransformationPlanner(runtime).Assess(State(), CardId,
            CardTransformationOperation.Apply, upgradeId: "grammar").Diagnostics).Code);
        Assert.Empty(new CardTransformationPlanner(runtime).Options(State()).Value);
    }

    [Fact]
    public void Grammar_InvalidRequirementsLimitsAndEffectlessBundlesFailAtSealing()
    {
        var contradictory = GrammarUpgrade(Rule()) with { Requirements = new() { RequiredTags = ["x"], ExcludedTags = ["x"] } };
        Assert.True(CardBundleCompiler.Seal(contradictory, Runtime([contradictory], Bundle(1))).IsFailure);
        var duplicate = GrammarUpgrade(Rule(), Rule());
        Assert.True(CardBundleCompiler.Seal(duplicate, Runtime([duplicate], Bundle(1))).IsFailure);
        var excessive = GrammarUpgrade(Enumerable.Range(0, 33).Select(index => Rule("r" + index)).ToArray());
        Assert.True(CardBundleCompiler.Seal(excessive, Runtime([excessive], Bundle(1))).IsFailure);
        var upgrade = GrammarUpgrade(Rule());
        Assert.True(CardBundleCompiler.Seal(upgrade, Runtime([upgrade], Bundle(1) with { Components =
            [new CardConditionComponentDefinition { ComponentId = "condition", Expression = "true" }] })).IsFailure);
        Assert.True(CardBundleCompiler.Seal(upgrade, Runtime([upgrade], Bundle(1) with { Components =
            Enumerable.Range(0, 65).Select(index => Impact(1) with { ComponentId = "e" + index }).ToArray() })).IsFailure);
    }

    [Fact]
    public void Grammar_RoundTripAndTenProjectionsPreserveSnapshotsAndHashes()
    {
        var runtime = Runtime([GrammarUpgrade(Rule())], Bundle(1)); var card = ApplyGrammar(runtime);
        var restored = JsonSerializer.Deserialize<CardInstanceState>(JsonSerializer.Serialize(card))!;
        Assert.Equal(CanonicalJson.ComputeHash(card), CanonicalJson.ComputeHash(restored));
        var changedCatalog = Runtime([GrammarUpgrade(Rule())], Bundle(9));
        var outputs = Enumerable.Range(0, 10).Select(_ => Resolve(restored, changedCatalog)).ToArray();
        Assert.Single(outputs.Select(output => output.Fingerprint).Distinct());
        Assert.Equal(1, Assert.Single(outputs[0].All<CardEffectComponentDefinition>()[0].Effect.ChainedEffects!).FlatValue);
        Assert.Equal(Resolve(card, runtime).Fingerprint, outputs[0].Fingerprint);
    }

    [Theory]
    [InlineData(CardCompositionScope.BeforeSequence, 4)]
    [InlineData(CardCompositionScope.AfterImpact, 6)]
    [InlineData(CardCompositionScope.AfterSequence, 4)]
    public void Grammar_CommonExecutorObservesRepeatAndScopeWithoutNewEffectProcessor(CardCompositionScope scope, int steps)
    {
        var card = Base() with { Components = [Impact(1) with { Effect = Impact(1).Effect with { Repeat = 3, TargetResource = "focus" } }] };
        var bundle = Bundle(1) with { Components = [Impact(1) with { Effect = Impact(1).Effect with { TargetResource = "focus" } }] };
        var runtime = Runtime([GrammarUpgrade(Rule(scope: scope))], bundle, card);
        var closed = CardBundleCompiler.Seal(GrammarUpgrade(Rule(scope: scope)), runtime).Value;
        var transformed = CardInstanceUpgradeTransitions.Apply(State().Deck.Topology.Instances[CardId], closed, Revision).Value;
        var effective = Resolve(transformed, runtime);
        var effects = effective.All<CardEffectComponentDefinition>().Select(component => component.Effect).ToArray();
        var request = EffectTransactionTests.Request(effects);
        var results = Enumerable.Range(0, 10).Select(_ => EffectTransactionTests.Executor().Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Equal(steps, results[0].Value.Steps.Length);
        Assert.Equal(10 - steps, results[0].Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
    }

    [Fact]
    public void Grammar_ParameterizedAffinityExecutesThroughTheConfiguredBucketPipeline()
    {
        var bundle = new CardComponentBundleDefinition { BundleId = "pulse", EffectComponentParameters = ["base"], Components =
            [new CardEffectComponentDefinition { ComponentId = "residual", Effect = StackPayloadTests.Apply(1) with
                { PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "$base" }] } }] };
        var rule = Rule() with { EffectBindings = new Dictionary<string, string> { ["base"] = "$anchor" } };
        var runtime = Runtime([GrammarUpgrade(rule)], bundle);
        var card = CardInstanceUpgradeTransitions.Apply(State().Deck.Topology.Instances[CardId],
            CardBundleCompiler.Seal(GrammarUpgrade(rule), runtime).Value, Revision).Value;
        card = CardInstanceUpgradeTransitions.Apply(card, Numeric("plus", 1), Revision).Value;
        var effective = Resolve(card, runtime);
        var fixture = StackPayloadTests.Fixture();
        var root = effective.All<CardEffectComponentDefinition>()[0].Effect with
            { TargetResource = "focus", CalculationChannel = "magnitude", CalculationPipelineId = "payload" };
        var request = EffectTransactionTests.Request(root) with { Run = fixture.Run, Card = effective };
        var results = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        var lot = Assert.Single(Assert.Single(results[0].Value.State.StatusEffects["enemy"]).PayloadLots);
        Assert.Equal(7, lot.Parameters["potency"].Definition.Numeric.FlatValue);
        Assert.Equal(17, lot.Parameters["potency"].Snapshot!.Value);
        Assert.Equal(effective.Fingerprint, lot.CardFingerprint);
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
    }

    [Fact]
    public void Grammar_LimitsMatchesBeforeUnboundedExpansion()
    {
        var upgrade = GrammarUpgrade(Enumerable.Range(0, 17).Select(index => Rule("r" + index)).ToArray());
        var runtime = Runtime([upgrade], Bundle(1), Base() with { Components =
            Enumerable.Range(0, 16).Select(index => Impact(1) with { ComponentId = "impact" + index }).ToArray() });
        var assessment = new CardTransformationPlanner(runtime).Assess(State(), CardId, CardTransformationOperation.Apply, upgradeId: "grammar");
        Assert.False(assessment.IsCompatible); Assert.Contains(assessment.Diagnostics, item => item.Code == "match_limit");
    }

    [Fact]
    public void Grammar_LimitsTotalGeneratedChildrenNotOnlyRootMemberCount()
    {
        var upgrade = GrammarUpgrade(Enumerable.Range(0, 9).Select(index => Rule("r" + index)).ToArray());
        var bundle = Bundle(1) with { Components = Enumerable.Range(0, 16).Select(index => Impact(1) with
            { ComponentId = "member" + index, Effect = Impact(1).Effect with { ChainedEffects = Enumerable.Range(0, 30).Select(_ => Impact(1).Effect).ToArray() } }).ToArray() };
        var runtime = Runtime([upgrade], bundle);
        var assessment = new CardTransformationPlanner(runtime).Assess(State(), CardId, CardTransformationOperation.Apply, upgradeId: "grammar");
        Assert.Equal("expansion_limit", Assert.Single(assessment.Diagnostics).Code);
    }

    [Fact]
    public void Grammar_SkippedParentDoesNotEmitResidualButSequenceEffectRemainsIndependent()
    {
        foreach (var scope in new[] { CardCompositionScope.AfterImpact, CardCompositionScope.AfterSequence })
        {
            var runtime = Runtime([GrammarUpgrade(Rule(scope: scope))], Bundle(1), Base() with
                { Components = [Impact(1) with { Effect = Impact(1).Effect with { Chance = 0 } }] });
            var effective = Resolve(ApplyGrammar(runtime), runtime);
            var effects = effective.All<CardEffectComponentDefinition>().Select(component => component.Effect with { TargetResource = "focus",
                ChainedEffects = component.Effect.ChainedEffects?.Select(child => child with { TargetResource = "focus" }).ToArray() }).ToArray();
            var result = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(effects));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(scope == CardCompositionScope.AfterImpact ? 0 : 1, result.Value.Records.Count);
        }
    }

    [Fact]
    public void Grammar_CannotEnterLedgerWithoutPinnedSealing()
    {
        var unsealed = GrammarUpgrade(Rule()) with { Patches = Numeric("plus", 1).Patches };
        Assert.True(CardInstanceUpgradeTransitions.Apply(State().Deck.Topology.Instances[CardId], unsealed, Revision).IsFailure);
        Assert.True(new EffectiveCardResolver().ValidateUpgrade(new CardContentCompiler().Compile(Base()).Value, unsealed).IsFailure);
        var runtime = Runtime([GrammarUpgrade(Rule())], Bundle(1));
        var crossRevision = State() with { Determinism = DeterministicContext.Create(42, "other-revision") };
        var assessment = new CardTransformationPlanner(runtime).Assess(crossRevision, CardId, CardTransformationOperation.Apply, upgradeId: "grammar");
        Assert.False(assessment.IsCompatible); Assert.Equal("other-revision", assessment.ContentRevision);
    }

    [Fact]
    public void Grammar_CopiesAuthorCollectionsAndSurvivesJsonRoundTrip()
    {
        var tags = new List<string> { "attack" }; var rules = new List<CardCompositionRuleDefinition> { Rule() };
        var parameters = new List<string> { "base" }; var bindings = new Dictionary<string, string> { ["base"] = "$anchor" };
        var predicate = new CardCompositionPredicate { RequiredTags = tags };
        var upgrade = GrammarUpgrade() with { CompositionRules = rules, Requirements = predicate };
        var bundle = Bundle(1) with { EffectComponentParameters = parameters };
        var rule = Rule() with { EffectBindings = bindings };
        tags.Clear(); rules.Clear(); parameters.Clear(); bindings.Clear();
        Assert.Single(upgrade.CompositionRules); Assert.Single(predicate.RequiredTags);
        Assert.Single(bundle.EffectComponentParameters); Assert.Single(rule.EffectBindings);
        Assert.Equal(CanonicalJson.ComputeHash(upgrade), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<CardUpgradeDefinition>(JsonSerializer.Serialize(upgrade))!));
    }
}
