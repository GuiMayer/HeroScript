using System.Collections.Immutable;
using System.Text.Json;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Core.Calculations;
using Core.Math;
using Core.Tests.Effects;
using Moq;
using Core.CardZones;
using Core.Content;
using Core.Common;
using Core.Config;
using Core.Resources;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardTransformationTests
{
    private const string Revision = "revision-a";
    private readonly EffectiveCardResolver _resolver = new();

    [Fact]
    public void ReplaceAffinity_PreservesIdentityNumericUpgradeAndHistory()
    {
        var original = Card();
        var improved = Apply(original, Upgrade("base-plus", Numeric(3)));
        var venom = Apply(improved, Upgrade("venom", new CardTagsPatchDefinition
            { Remove = ["fire"], Add = ["venom"] }) with { Category = CardTransformationCategory.Affinity });
        var ice = CardInstanceUpgradeTransitions.Replace(venom, 2,
            Upgrade("ice", new CardTagsPatchDefinition { Remove = ["fire"], Add = ["ice"] }) with
                { Category = CardTransformationCategory.Affinity }, Revision).Value;
        var resolved = Resolve(ice);

        Assert.Equal(original.CardInstanceId, ice.CardInstanceId);
        Assert.Equal(original.DefinitionId, ice.DefinitionId);
        Assert.Equal(9, Amount(resolved));
        Assert.Equal(new[] { "attack", "ice" }, resolved.Tags);
        Assert.Equal(new ulong[] { 1, 3 }, resolved.AppliedUpgrades.Select(item => item.TransformationId));
        Assert.Equal(3, ice.Upgrades.Count);
        Assert.Equal(2UL, ice.Upgrades[2].TargetTransformationId);
        Assert.Equal(CardTransformationOperation.Replace, ice.Upgrades[2].Operation);
        Assert.Equal(Revision, ice.Upgrades[2].ContentRevision);
        Assert.Equal(new[] { "attack", "venom" }, Resolve(venom).Tags);
        Assert.Equal(6, Amount(Resolve(original)));
    }

    [Fact]
    public void ReplaceBehavior_KeepsItsOriginalPositionBeforeLaterBaseUpgrades()
    {
        var first = Apply(Card(), Upgrade("behavior", Replace(Effect(8))) with
            { Category = CardTransformationCategory.Behavior });
        var improved = Apply(first, Upgrade("plus", Numeric(3)));
        var next = CardInstanceUpgradeTransitions.Replace(improved, 1,
            Upgrade("other-behavior", Replace(Effect(12))) with { Category = CardTransformationCategory.Behavior }, Revision).Value;

        Assert.Equal(15, Amount(Resolve(next)));
        Assert.Equal(new ulong[] { 3, 2 }, Resolve(next).AppliedUpgrades.Select(item => item.TransformationId));
        var removed = CardInstanceUpgradeTransitions.Remove(next, 3, Revision).Value;
        Assert.Equal(9, Amount(Resolve(removed)));
        Assert.Equal(4, removed.Upgrades.Count);
        Assert.Equal(1, CardTransformationLedger.Count(removed));
    }

    [Fact]
    public void RemoveMultiplicationByZero_RebuildsInsteadOfInvertingThePatch()
    {
        var zero = Apply(Card(), Upgrade("zero", Numeric(0, CardNumericPatchOperation.Multiply)));
        var added = Apply(zero, Upgrade("plus", Numeric(2)));
        var removed = CardInstanceUpgradeTransitions.Remove(added, 1, Revision).Value;

        Assert.Equal(2, Amount(Resolve(added)));
        Assert.Equal(8, Amount(Resolve(removed)));
        Assert.Equal(3UL, removed.Upgrades.Last().TransformationId);
    }

    [Fact]
    public void AddAndRemoveComponents_ProducesCanonicalOrderAndCanBeRemovedAsOneTransformation()
    {
        var original = Card();
        var changed = Apply(original, Upgrade("dual", new CardComponentPatchDefinition
        {
            ComponentId = "secondary", Operation = CardComponentPatchOperation.Add,
            Component = Effect(2) with { ComponentId = "secondary", Order = -10 }
        }, new CardComponentPatchDefinition { ComponentId = "impact", Operation = CardComponentPatchOperation.Remove }));

        Assert.Equal("secondary", Assert.Single(Resolve(changed).Components).ComponentId);
        var restored = CardInstanceUpgradeTransitions.Remove(changed, 1, Revision).Value;
        Assert.Equal("impact", Assert.Single(Resolve(restored).Components).ComponentId);
        Assert.NotEqual(Resolve(original).Fingerprint, Resolve(restored).Fingerprint);
        Assert.Equal(0, CardTransformationLedger.Count(restored));
        Assert.Empty(original.Upgrades);
    }

    [Fact]
    public void TwoPhysicalCopiesAndForks_DoNotShareMutations()
    {
        var source = Card();
        var copy = source with { CardInstanceId = Guid.Parse("20000000-0000-8000-8000-000000000002") };
        var branch = Apply(source, Upgrade("boost", Numeric(5)));
        var fork = CardInstanceUpgradeTransitions.Remove(branch with { }, 1, Revision).Value;

        Assert.Equal(11, Amount(Resolve(branch)));
        Assert.Equal(6, Amount(Resolve(source)));
        Assert.Equal(6, Amount(Resolve(copy)));
        Assert.Equal(6, Amount(Resolve(fork)));
        Assert.Empty(source.Upgrades);
        Assert.Single(branch.Upgrades);
        Assert.Equal(2, fork.Upgrades.Count);
        Assert.NotEqual(Resolve(copy).Fingerprint, Resolve(source).Fingerprint);
    }

    [Fact]
    public void LedgerRoundTrip_WithPolymorphicPatchesAndTombstone_PreservesHash()
    {
        var state = Apply(Card(), Upgrade("tags", new CardTagsPatchDefinition { Remove = ["fire"], Add = ["ice"] }));
        state = Apply(state, Upgrade("replace", Replace(Effect(10))));
        state = CardInstanceUpgradeTransitions.Remove(state, 1, Revision).Value;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<CardInstanceState>(JsonSerializer.Serialize(state, options), options)!;

        Assert.Equal(CanonicalJson.ComputeHash(state), CanonicalJson.ComputeHash(restored));
        Assert.Equal(Resolve(state).Fingerprint, Resolve(restored).Fingerprint);
        Assert.IsType<CardTagsPatchDefinition>(state.Upgrades[0].Patches[0]);
        Assert.IsType<CardComponentPatchDefinition>(restored.Upgrades[1].Patches[0]);
        Assert.Equal(10, Amount(Resolve(restored)));
        Assert.Equal(new[] { "attack", "fire" }, Resolve(restored).Tags);
    }

    [Fact]
    public void TenIdenticalHistories_ProduceIdenticalIdentitiesTracesAndHashes()
    {
        var outputs = Enumerable.Range(0, 10).Select(_ =>
        {
            var state = Apply(Card(), Upgrade("plus", Numeric(3)));
            state = Apply(state, Upgrade("tags", new CardTagsPatchDefinition { Remove = ["fire"], Add = ["ice"] }));
            state = CardInstanceUpgradeTransitions.Replace(state, 2,
                Upgrade("venom", new CardTagsPatchDefinition { Remove = ["fire"], Add = ["venom"] }), Revision).Value;
            return CanonicalJson.ComputeHash(new { State = state, Effective = Resolve(state) });
        }).ToArray();

        Assert.Single(outputs.Distinct());
    }

    [Fact]
    public void Removal_FreesApplicationCapacityButNeverReusesAnIdentity()
    {
        var definition = Upgrade("plus", Numeric(3));
        var first = Apply(Card(), definition);
        Assert.True(CardInstanceUpgradeTransitions.Apply(first, definition, Revision).IsFailure);
        var removed = CardInstanceUpgradeTransitions.Remove(first, 1, Revision).Value;
        var reapplied = Apply(removed, definition);

        Assert.Equal(new ulong[] { 1, 2, 3 }, reapplied.Upgrades.Select(item => item.TransformationId));
        Assert.Equal(1, CardTransformationLedger.Count(reapplied, "plus"));
        Assert.Equal(9, Amount(Resolve(reapplied)));
        Assert.True(CardInstanceUpgradeTransitions.Remove(reapplied, 1, Revision).IsFailure);

        var run = new RunState
        {
            ConfigName = "test", Determinism = DeterministicContext.Create(42, Revision),
            ResolvedMode = new() { ProgressionPolicy = new() { AllowOutOfActivityCommands = true } },
            Deck = TestCardZones.WithInstance("cards", removed)
        };
        var view = Assert.Single(Assert.Single(CardZoneReadModel.Project(run).Zones).Cards);
        Assert.Empty(view.Upgrades);
        Assert.Equal(2, view.TransformationLedger.Count);
        var option = Assert.Single(new CardTransformationPlanner(Runtime(definition)).Options(run).Value);
        Assert.Equal("plus", option.UpgradeId);
    }

    [Fact]
    public void RemovingARequiredStructuralTransformation_RejectsTheRebuiltComposition()
    {
        var added = Apply(Card(), Upgrade("extra", new CardComponentPatchDefinition
        {
            ComponentId = "extra", Operation = CardComponentPatchOperation.Add,
            Component = Effect(2) with { ComponentId = "extra" }
        }));
        var improved = Apply(added, Upgrade("plus", Numeric(3) with { ComponentId = "extra" }));
        var candidate = CardInstanceUpgradeTransitions.Remove(improved, 1, Revision).Value;
        var result = _resolver.Resolve(Definition(), candidate);

        Assert.True(result.IsFailure);
        Assert.Contains("Component not found: extra", result.Error);
        Assert.Equal(2, improved.Upgrades.Count);
        Assert.Equal(2, Resolve(improved).Components.Count);
    }

    [Theory]
    [InlineData(CardComponentPatchOperation.Add, "impact", "impact")]
    [InlineData(CardComponentPatchOperation.Replace, "missing", "missing")]
    [InlineData(CardComponentPatchOperation.Replace, "impact", "renamed")]
    [InlineData(CardComponentPatchOperation.Remove, "missing", null)]
    [InlineData(CardComponentPatchOperation.Remove, "impact", "impact")]
    [InlineData((CardComponentPatchOperation)999, "impact", "impact")]
    public void InvalidComponentOperation_FailsClosed(CardComponentPatchOperation operation, string addressed, string? componentId)
    {
        var candidate = Apply(Card(), Upgrade("invalid", new CardComponentPatchDefinition
        {
            Operation = operation, ComponentId = addressed,
            Component = componentId == null ? null : Effect(1) with { ComponentId = componentId }
        }));
        Assert.True(_resolver.Resolve(Definition(), candidate).IsFailure);
    }

    [Theory]
    [InlineData("missing", "ice")]
    [InlineData("fire", "attack")]
    [InlineData("fire", "fire")]
    [InlineData("fire", "")]
    public void ConflictingTagChanges_AreNotSilentlyApplied(string remove, string add)
    {
        var candidate = Apply(Card(), Upgrade("invalid", new CardTagsPatchDefinition { Remove = [remove], Add = [add] }));
        Assert.True(_resolver.Resolve(Definition(), candidate).IsFailure);
    }

    [Fact]
    public void RemovingAReferencedComponent_FailsIncludingBindingsInsideTriggers()
    {
        var definition = Definition(new CardTriggerComponentDefinition
        {
            ComponentId = "trigger", Boundary = "after.action", Effects =
            [new() { Type = EffectType.APPLY_STATUS, StatusId = "burn", PayloadBindings =
                [new() { ParameterId = "potency", CardEffectComponentId = "impact" }] }]
        });
        var candidate = Apply(Card(), Upgrade("remove", new CardComponentPatchDefinition
            { ComponentId = "impact", Operation = CardComponentPatchOperation.Remove }));

        var result = _resolver.Resolve(definition, candidate);
        Assert.True(result.IsFailure);
        Assert.Contains("payload binding", result.Error);
    }

    [Fact]
    public void DuplicateOutputAliases_AreRejectedAfterStructuralAddition()
    {
        var definition = Definition(Effect(2) with
            { ComponentId = "other", Effect = Effect(2).Effect with { OutputId = "hit" } });
        var candidate = Apply(Card(), Upgrade("alias", Replace(Effect(6) with
            { Effect = Effect(6).Effect with { OutputId = "hit" } })));

        var result = _resolver.Resolve(definition, candidate);
        Assert.True(result.IsFailure);
        Assert.Contains("duplicate outputId", result.Error);
    }

    [Fact]
    public void MultipleTargetingComponents_AreRejectedAfterStructuralAddition()
    {
        var definition = Definition(new CardTargetingComponentDefinition { ComponentId = "targets" });
        var candidate = Apply(Card(), Upgrade("targets", new CardComponentPatchDefinition
        {
            ComponentId = "other-targets", Operation = CardComponentPatchOperation.Add,
            Component = new CardTargetingComponentDefinition { ComponentId = "other-targets" }
        }));
        Assert.True(_resolver.Resolve(definition, candidate).IsFailure);
    }

    [Fact]
    public void NullEffectPayload_IsRejectedBeforeAnIntermediateNumericPatchCanDereferenceIt()
    {
        var structural = Replace(Effect(6) with { Effect = null! });
        var candidate = Apply(Card(), Upgrade("null-effect", structural, Numeric(1)));
        Assert.True(_resolver.Resolve(Definition(), candidate).IsFailure);
        Assert.True(_resolver.Resolve(Definition(), Apply(Card(), Upgrade("null-effect", structural))).IsFailure);
    }

    [Theory]
    [InlineData(float.NaN, "pay")]
    [InlineData(-1, "pay")]
    [InlineData(1, "")]
    public void InvalidAlternativeCosts_AreRejectedByTheCommonCompiler(float amount, string optionId)
    {
        var candidate = Apply(Card(), Upgrade("cost", new CardComponentPatchDefinition
        {
            ComponentId = "cost", Operation = CardComponentPatchOperation.Add,
            Component = new CardCostComponentDefinition
            {
                ComponentId = "cost", Costs = new()
                {
                    AlternativeCosts = [new() { OptionId = optionId, Costs = [new() { ResourceId = "health", Amount = amount }] }]
                }
            }
        }));
        Assert.True(_resolver.Resolve(Definition(), candidate).IsFailure);
    }

    [Fact]
    public void HotReload_ValidatesResourceReferencesOfAlternativeCostOptions()
    {
        var candidate = Apply(Card(), Upgrade("cost", new CardComponentPatchDefinition
        {
            ComponentId = "cost", Operation = CardComponentPatchOperation.Add,
            Component = new CardCostComponentDefinition
            {
                ComponentId = "cost", Costs = new() { AlternativeCosts =
                    [new() { OptionId = "pay", Costs = [new() { ResourceId = "missing", Amount = 1 }] }] }
            }
        }));
        var result = RunContentCompatibilityValidator.ValidateForActivation(
            new RunState { Deck = TestCardZones.WithInstance("cards", candidate) }, Runtime(null));
        Assert.True(result.IsFailure);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public void TypedParameterUpgrade_ChangesItsBaseAndPreservesProfileAndTrace()
    {
        var numeric = new EffectNumericParameterDefinition
        {
            Parameter = EffectNumericParameter.Amount, FlatValue = 4,
            Channel = "damage", PipelineId = "profile", UnitId = "scalar", StageIds = ["origin"]
        };
        var definition = Definition(Effect(0) with
        {
            ComponentId = "typed", Effect = Effect(0).Effect with { FlatValue = null, Parameters = [numeric] }
        });
        var candidate = Apply(Card(), Upgrade("parameter", new CardEffectParameterNumericPatchDefinition
            { ComponentId = "typed", Parameter = EffectNumericParameter.Amount, Value = 3 }));
        var resolved = _resolver.Resolve(definition, candidate).Value;
        var parameter = resolved.All<CardEffectComponentDefinition>().Single(item => item.ComponentId == "typed").Effect.Parameters[0];

        Assert.Equal(numeric with { FlatValue = 7 }, parameter);
        var trace = Assert.Single(resolved.UpgradeTrace);
        Assert.Equal("Amount.FlatValue", trace.Attribute);
        Assert.Equal(4, trace.PreviousValue);
        Assert.Equal(7, trace.CurrentValue);
        Assert.Equal(1UL, trace.TransformationId);
        Assert.Equal(4, numeric.FlatValue);
        var wrongPatch = Apply(Card(), Upgrade("old-field", Numeric(2) with { ComponentId = "typed" }));
        Assert.True(_resolver.Resolve(definition, wrongPatch).IsFailure);

        var calculation = new CalculationResolver(new Mock<IRuntimeFormulaEvaluator>(MockBehavior.Strict).Object,
            engine: new CalculationEngine(), allowUnconfiguredCalculations: true).ResolveParameter(
            resolved.All<CardEffectComponentDefinition>().Single(item => item.ComponentId == "typed").Effect,
            parameter, "parameter-upgrade", new CalculationSourceContext
            {
                ContentRevision = Revision, Card = resolved, ComponentId = "typed",
                Actor = GameplayOwnershipTests.State().GetActor("hero"), CaptureOnly = true,
                Pipeline = new()
                {
                    PipelineId = "profile", Channel = "damage", UnitId = "scalar",
                    Stages = [new() { StageId = "origin", Scope = CalculationStageScope.Actor }],
                    Buckets = [new() { BucketId = "base", StageId = "origin" }]
                }
            });
        Assert.True(calculation.IsSuccess, calculation.IsFailure ? calculation.Error : null);
        Assert.Equal(7, calculation.Value.Value);
        var baseTrace = calculation.Value.Calculation!.BaseTrace;
        Assert.Equal(2, baseTrace.Count);
        Assert.Equal(4, baseTrace[0].Output);
        Assert.Equal(CalculationSourceKind.Upgrade, baseTrace[1].SourceKind);
        Assert.Equal(4, baseTrace[1].Input);
        Assert.Equal(7, baseTrace[1].Output);
    }

    [Fact]
    public void TypedParameterUpgrade_CannotRewriteAnInputQuantityOrCreateAMissingParameter()
    {
        var definition = Definition(Effect(0) with
        {
            ComponentId = "typed", Effect = Effect(0).Effect with
            {
                FlatValue = null, Parameters = [new() { Parameter = EffectNumericParameter.Amount,
                    InputQuantityId = "payload.potency", Channel = "damage", UnitId = "scalar" }]
            }
        });
        var candidate = Apply(Card(), Upgrade("parameter", new CardEffectParameterNumericPatchDefinition
            { ComponentId = "typed", Parameter = EffectNumericParameter.Amount, Value = 3 }));
        Assert.True(_resolver.Resolve(definition, candidate).IsFailure);
        Assert.True(_resolver.Resolve(Definition(), Apply(Card(), Upgrade("missing", new CardEffectParameterNumericPatchDefinition
            { ComponentId = "impact", Parameter = EffectNumericParameter.Amount, Value = 3 }))).IsFailure);
    }

    [Theory]
    [InlineData(0UL, "revision-a", CardTransformationCategory.Base)]
    [InlineData(1UL, "", CardTransformationCategory.Base)]
    [InlineData(1UL, "revision-a", (CardTransformationCategory)999)]
    public void InvalidLedgerMetadata_FailsResolution(ulong id, string revision, CardTransformationCategory category)
    {
        var card = Apply(Card(), Upgrade("plus", Numeric(3)));
        card = card with { Upgrades = [card.Upgrades[0] with { TransformationId = id, ContentRevision = revision, Category = category }] };
        Assert.True(_resolver.Resolve(Definition(), card).IsFailure);
    }

    [Fact]
    public void DuplicateLedgerIdentityAndInactiveRemoval_FailCausalityValidation()
    {
        var first = Apply(Card(), Upgrade("first", Numeric(1)));
        var second = Apply(first, Upgrade("second", Numeric(2)));
        var invalid = second with { Upgrades = [second.Upgrades[0], second.Upgrades[1] with { TransformationId = 1 }] };
        Assert.True(CardTransformationLedger.Project(invalid.Upgrades).IsFailure);
        var removed = CardInstanceUpgradeTransitions.Remove(first, 1, Revision).Value;
        Assert.True(CardInstanceUpgradeTransitions.Replace(removed, 1, Upgrade("new", Numeric(3)), Revision).IsFailure);
        Assert.True(CardInstanceUpgradeTransitions.Remove(removed, 1, Revision).IsFailure);
    }

    [Fact]
    public void OlderEngineSnapshot_IsRejectedByVersionBeforeTryingToInterpretItsLedger()
    {
        var (manager, state) = Manager(Runtime(null));
        var oldCard = Card() with { Upgrades = [new CardUpgradeState { UpgradeId = "old-format" }] };
        var old = state with
        {
            Determinism = DeterministicContext.Create(42, Revision, "12"),
            Deck = TestCardZones.WithInstance("cards", oldCard)
        };
        var result = manager.HydrateForReplay(old);
        Assert.True(result.IsFailure);
        Assert.Contains("Engine version unavailable", result.Error);
        Assert.Equal(CanonicalJson.ComputeHash(state), CanonicalJson.ComputeHash(manager.GetRun(state.RunId).Value));
    }

    [Fact]
    public void CallerOwnedLists_AreDefensivelyCopiedByDefinitionsAndLedger()
    {
        var tags = new List<string> { "ice" };
        var patches = new List<CardUpgradePatchDefinition> { new CardTagsPatchDefinition { Remove = ["fire"], Add = tags } };
        var upgrade = new CardUpgradeDefinition { UpgradeId = "ice", Patches = patches };
        var card = Apply(Card(), upgrade);
        var ledger = card.Upgrades.ToList();
        var snapshot = card with { Upgrades = ledger };
        tags.Clear(); patches.Clear(); ledger.Clear();

        Assert.Equal(new[] { "attack", "ice" }, Resolve(snapshot).Tags);
        Assert.Single(snapshot.Upgrades);
    }

    [Fact]
    public void LedgerLimit_RejectsInsteadOfTruncatingHistory()
    {
        var card = Card() with
        {
            Upgrades = Enumerable.Range(1, CardTransformationLedger.MaximumEntries).Select(index =>
                new CardUpgradeState { TransformationId = (ulong)index, UpgradeId = "noop", ContentRevision = Revision }).ToArray()
        };
        Assert.True(CardInstanceUpgradeTransitions.Apply(card, Upgrade("plus", Numeric(1)), Revision).IsFailure);
        Assert.Equal(CardTransformationLedger.MaximumEntries, card.Upgrades.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpgradeCommand_RejectsInvalidCompositionOrReferenceWithoutPublishingCandidate(bool collision)
    {
        var definition = Upgrade("invalid", new CardComponentPatchDefinition
        {
            ComponentId = collision ? "impact" : "secondary", Operation = CardComponentPatchOperation.Add,
            Component = Effect(2) with { ComponentId = collision ? "impact" : "secondary",
                Effect = Effect(2).Effect with { TargetResource = collision ? "health" : "missing" } }
        });
        var runtime = Runtime(definition);
        var (manager, state) = Manager(runtime);
        var hash = CanonicalJson.ComputeHash(state);
        var command = new RunCommandIdentity(Guid.Parse("30000000-0000-8000-8000-000000000001"),
            RunCommandTypes.UpgradeCard, state.Sequence, state.Determinism.Step);
        var result = manager.Execute(state.RunId, new GameplayCommandEnvelope(command,
            JsonSerializer.SerializeToElement(new CardUpgradeCommand(Card().CardInstanceId, "invalid"))));

        Assert.True(result.IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(manager.GetRun(state.RunId).Value));
        Assert.Empty(manager.GetRun(state.RunId).Value.Deck.GetCard(Card().CardInstanceId)!.Upgrades);
        Assert.Null(manager.FindReceipt(state.RunId, command.CommandId).Value);
    }

    [Fact]
    public void UpgradeCommand_StoresRevisionedStructuralSnapshotAndRetryIsIdempotent()
    {
        var runtime = Runtime(Upgrade("ice", new CardTagsPatchDefinition { Remove = ["fire"], Add = ["ice"] }) with
            { Category = CardTransformationCategory.Affinity });
        var (manager, state) = Manager(runtime);
        var command = new RunCommandIdentity(Guid.Parse("30000000-0000-8000-8000-000000000001"),
            RunCommandTypes.UpgradeCard, state.Sequence, state.Determinism.Step);
        var payload = JsonSerializer.SerializeToElement(new CardUpgradeCommand(Card().CardInstanceId, "ice"));
        var applied = manager.Execute(state.RunId, new GameplayCommandEnvelope(command, payload));
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        var card = applied.Value.State.Deck.GetCard(Card().CardInstanceId)!;

        Assert.Equal(new[] { "attack", "ice" }, Resolve(card).Tags);
        Assert.Equal(Revision, Assert.Single(card.Upgrades).ContentRevision);
        Assert.Equal(CardTransformationCategory.Affinity, card.Upgrades[0].Category);
        var retry = manager.Execute(state.RunId, new GameplayCommandEnvelope(command, payload));
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
        Assert.True(retry.Value.Duplicate);
        Assert.Equal(applied.Value.StateHash, retry.Value.StateHash);
        Assert.Single(retry.Value.State.Deck.GetCard(card.CardInstanceId)!.Upgrades);
    }

    [Fact]
    public void HotReload_RejectsRemovedReferenceInsideAnOwnedStructuralSnapshot()
    {
        var definition = Upgrade("new-resource", new CardComponentPatchDefinition
        {
            ComponentId = "secondary", Operation = CardComponentPatchOperation.Add,
            Component = Effect(2) with { ComponentId = "secondary", Effect = Effect(2).Effect with { TargetResource = "missing" } }
        });
        var runtime = Runtime(null);
        var card = Apply(Card(), definition);
        var run = new RunState { Deck = TestCardZones.WithInstance("cards", card) };
        Assert.True(new CardContentCompiler().Compile("strike", runtime).IsSuccess);
        Assert.True(_resolver.Resolve(Definition(), card).IsSuccess);

        var validation = RunContentCompatibilityValidator.ValidateForActivation(run, runtime);
        Assert.True(validation.IsFailure);
        Assert.Contains("missing", validation.Error);
        Assert.Single(card.Upgrades);
    }

    private static (RunManager Manager, RunState State) Manager(ContentRuntime runtime)
    {
        var runtimes = new Mock<IContentRuntimeResolver>(MockBehavior.Strict);
        runtimes.Setup(item => item.Resolve(Revision, "test")).Returns(Result<ContentRuntime>.Success(runtime));
        var manager = new RunManager(new Mock<IConfigManager>().Object, new Mock<IResourceLoader>().Object,
            contentRuntimes: runtimes.Object);
        var state = new RunState
        {
            RunId = Guid.Parse("40000000-0000-8000-8000-000000000001"), ConfigName = "test", Sequence = 1,
            Deck = TestCardZones.WithInstance("cards", Card()), Determinism = DeterministicContext.Create(42, Revision),
            ResolvedMode = new() { ProgressionPolicy = new() { AllowOutOfActivityCommands = true } }
        };
        var hydrated = manager.HydrateForReplay(state);
        Assert.True(hydrated.IsSuccess, hydrated.IsFailure ? hydrated.Error : null);
        return (manager, state);
    }

    private static ContentRuntime Runtime(CardUpgradeDefinition? upgrade)
    {
        var artifacts = new List<(string Kind, string Path, Dictionary<string, object> Definitions)>
        {
            ("cards", "cards/catalog.json", new() { ["strike"] = new CardContentDefinition
                { CardId = "strike", Tags = ["attack", "fire"], Components = [Effect(6)] } }),
            ("resources", "resources/catalog.json", new() { ["health"] = new ResourceDefinition { ResourceId = "health" } })
        };
        if (upgrade != null) artifacts.Add(("card-upgrades", "card-upgrades/catalog.json", new() { [upgrade.UpgradeId] = upgrade }));
        var result = ContentRuntime.Create(new ContentBundle
        {
            Manifest = new() { ConfigName = "test", Revision = Revision, Artifacts = artifacts.Select(item =>
                new ContentArtifactManifest { Kind = item.Kind, Path = item.Path, DefinitionCount = item.Definitions.Count }).ToImmutableArray() },
            Artifacts = artifacts.ToImmutableDictionary(item => item.Path, item => JsonSerializer.SerializeToElement(item.Definitions))
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private EffectiveCardDefinition Resolve(CardInstanceState card)
    {
        var result = _resolver.Resolve(Definition(), card);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardInstanceState Card() => new()
    {
        CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"), DefinitionId = "strike"
    };

    private static CompiledCardDefinition Definition(params CardComponentDefinition[] extra)
    {
        var result = new CardContentCompiler().Compile(new CardContentDefinition
            { CardId = "strike", Tags = ["attack", "fire"], Components = new[] { Effect(6) }.Concat(extra).ToArray() });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardInstanceState Apply(CardInstanceState card, CardUpgradeDefinition definition)
    {
        var result = CardInstanceUpgradeTransitions.Apply(card, definition, Revision);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardUpgradeDefinition Upgrade(string id, params CardUpgradePatchDefinition[] patches) => new()
        { UpgradeId = id, CardDefinitionIds = ["strike"], Patches = patches };
    private static CardEffectComponentDefinition Effect(float value) => new()
        { ComponentId = "impact", Effect = new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = value } };
    private static CardComponentPatchDefinition Replace(CardComponentDefinition component) => new()
        { ComponentId = component.ComponentId, Operation = CardComponentPatchOperation.Replace, Component = component };
    private static CardEffectNumericPatchDefinition Numeric(float value, CardNumericPatchOperation operation = CardNumericPatchOperation.Add) => new()
        { ComponentId = "impact", Attribute = CardEffectNumericAttribute.FlatValue, Operation = operation, Value = value };
    private static float? Amount(EffectiveCardDefinition card) => card.All<CardEffectComponentDefinition>()
        .Single(item => item.ComponentId == "impact").Effect.FlatValue;
}
