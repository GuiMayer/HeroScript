using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Combat.LegalActions;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardInspectionServiceTests
{
    private const string Revision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly Guid CardId = Guid.Parse("10000000-0000-8000-8000-000000000001");
    private static readonly Guid CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001");

    [Fact]
    public void Inspect_FullModeReturnsEveryPinnedSourceAndExactPreview()
    {
        var run = Run(InspectionDetailLevel.Full);
        var runtime = Runtime();
        var compiled = new CompiledCardDefinition
        {
            CardId = "strike",
            Fingerprint = "compiled"
        };
        var upgrade = new CardUpgradeState { UpgradeId = "plus" };
        var effective = new EffectiveCardDefinition
        {
            CardInstanceId = CardId,
            DefinitionId = "strike",
            AppliedUpgrades = [upgrade],
            Fingerprint = "effective"
        };
        var evaluation = new CardPlayEvaluation
        {
            CardInstanceId = CardId,
            ActorId = "hero",
            IsLegal = true,
            LegalTargetIds = ["enemy"],
            ResolvedTargetIds = ["enemy"],
            Destination = CardConsumeDestination.Discard
        };
        var runs = new Mock<IRunManager>();
        runs.Setup(manager => manager.GetRunByCombat(CombatId))
            .Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(resolver => resolver.Resolve(Revision, "default"))
            .Returns(Result<ContentRuntime>.Success(runtime));
        var compiler = new Mock<ICardContentCompiler>();
        compiler.Setup(service => service.Compile("strike", runtime))
            .Returns(Result<CompiledCardDefinition>.Success(compiled));
        var effectiveCards = new Mock<IEffectiveCardResolver>();
        effectiveCards.Setup(service => service.Resolve(compiled, It.IsAny<CardInstanceState>()))
            .Returns(Result<EffectiveCardDefinition>.Success(effective));
        var legal = new Mock<ILegalActionResolver>();
        legal.Setup(service => service.Evaluate(
                run, It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), CombatCommandOrigin.PlayerInput))
            .Returns(Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
            {
                CardEvaluation = evaluation,
                Candidate = new LegalActionCandidate
                {
                    CandidateId = "candidate",
                    Command = new CombatActionCommand
                    {
                        RunId = run.RunId, ActorId = "hero", ActionType = ActionType.PLAY_CARD,
                        CardInstanceId = CardId, TargetIds = ["enemy"]
                    },
                    CardDefinitionId = "strike",
                    SuccessorRun = run,
                    SuccessorCombat = run.GetEncounter(CombatId)!.Combat,
                    ResolutionFingerprint = "exact-preview",
                    CardPlay = new CardPlayExecutionResult
                    {
                        Combat = run.GetEncounter(CombatId)!.Combat,
                        Card = effective,
                        Evaluation = evaluation,
                        Destination = CardConsumeDestination.Discard,
                        ResolutionFingerprint = "exact-preview"
                    }
                }
            }));
        var service = new CardInspectionService(
            runs.Object,
            runtimes.Object,
            compiler.Object,
            effectiveCards.Object,
            legal.Object);

        var first = service.Inspect(new CardInspectionRequest
        {
            CombatId = CombatId,
            CardInstanceId = CardId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        });
        var second = service.Inspect(new CardInspectionRequest
        {
            CombatId = CombatId,
            CardInstanceId = CardId,
            ActorId = "hero",
            SelectedTargetIds = ["enemy"]
        });

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal("exact-preview", first.Value.ResolutionFingerprint);
        Assert.Equal(first.Value.ResolutionFingerprint, second.Value.ResolutionFingerprint);
        Assert.Equal("strike", first.Value.BaseContainer.CardId);
        Assert.Equal("compiled", first.Value.CompiledContainer.Fingerprint);
        Assert.Equal("plus", Assert.Single(first.Value.AppliedUpgrades).UpgradeId);
        Assert.True(first.Value.IsPlayable);
        Assert.Equal("hand", first.Value.Zone);
        Assert.NotNull(first.Value.ContextSources);
        Assert.Single(first.Value.ContextSources!.Relics);
        Assert.Single(first.Value.ContextSources.Modifiers);
        Assert.Single(first.Value.ContextSources.Statuses["hero"]);
        Assert.Equal("enemy", Assert.Single(first.Value.ContextSources.CandidateTargets).InstanceId);
        legal.Verify(service => service.Evaluate(
            run, It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), CombatCommandOrigin.PlayerInput),
            Times.Exactly(2));
    }

    [Fact]
    public void Inspect_DisabledModeRejectsProjectionBeforeResolvingContent()
    {
        var run = Run(InspectionDetailLevel.Disabled);
        var runs = new Mock<IRunManager>();
        runs.Setup(manager => manager.GetRunByCombat(CombatId))
            .Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        var service = new CardInspectionService(
            runs.Object,
            runtimes.Object,
            Mock.Of<ICardContentCompiler>(),
            Mock.Of<IEffectiveCardResolver>(),
            Mock.Of<ILegalActionResolver>());

        var result = service.Inspect(new CardInspectionRequest
        {
            CombatId = CombatId,
            CardInstanceId = CardId
        });

        Assert.True(result.IsFailure);
        Assert.Contains("disabled", result.Error);
        runtimes.Verify(
            resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    private static RunState Run(InspectionDetailLevel detail)
    {
        var combat = new CombatState
        {
            CombatId = CombatId,
            Actors = new[] { Entity("hero", true), Entity("enemy", false) }
                .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero",
                WaitingForInput = true
            },
            PhaseState = new PhaseState
            {
                SequenceId = "phases",
                ContentRevision = Revision,
                Cursor = "main"
            },
            StatusEffects = new Dictionary<string, ImmutableArray<StatusEffectInstance>>
            {
                ["hero"] =
                [
                    new StatusEffectInstance
                    {
                        InstanceId = Guid.Parse("30000000-0000-8000-8000-000000000001"),
                        StatusId = "strength",
                        Definition = new StatusEffectDefinition(),
                        TargetId = "hero",
                        IsActive = true
                    }
                ]
            }.ToImmutableDictionary(StringComparer.Ordinal),
            Determinism = DeterministicContext.Create(2, Revision)
        };
        return new RunState
        {
            RunId = Guid.Parse("40000000-0000-8000-8000-000000000001"),
            ModeId = "sandbox",
            ConfigName = "default",
            ActiveEncounterId = CombatId,
            Encounters = [new RunEncounterState { Combat = combat }],
            Deck = new DeckState
            {
                CardInstances = new Dictionary<Guid, CardInstanceState>
                {
                    [CardId] = new() { CardInstanceId = CardId, DefinitionId = "strike" }
                },
                HandInstanceIds = [CardId]
            },
            Relics =
            [
                new RunRelicState
                {
                    RelicInstanceId = Guid.Parse("50000000-0000-8000-8000-000000000001"),
                    DefinitionId = "relic"
                }
            ],
            Modifiers =
            [
                new ScriptModifierInstance
                {
                    InstanceId = Guid.Parse("60000000-0000-8000-8000-000000000001"),
                    ModifierId = "buff"
                }
            ],
            ResolvedMode = new ResolvedGameMode
            {
                CapabilityPolicy = new CapabilityPolicyDefinition
                {
                    CardInspectionDetail = detail
                }
            },
            Determinism = DeterministicContext.Create(1, Revision)
        };
    }

    private static ContentRuntime Runtime()
    {
        const string path = "cards/catalog.json";
        var artifacts = new Dictionary<string, JsonElement>
        {
            [path] = JsonSerializer.SerializeToElement(new Dictionary<string, CardContentDefinition>
            {
                ["strike"] = new() { CardId = "strike" }
            })
        };
        var result = ContentRuntime.Create(new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default",
                Revision = Revision,
                Artifacts =
                [
                    new ContentArtifactManifest
                    {
                        Kind = "cards",
                        Path = path,
                        DefinitionCount = 1
                    }
                ]
            },
            Artifacts = artifacts.ToImmutableDictionary(StringComparer.Ordinal)
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CombatActorState Entity(string id, bool hero) => new()
    {
        InstanceId = id,
        SideId = hero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = hero ? ControllerKind.Player : ControllerKind.AI },
        ResourceState = new ResourceSet { OwnerId = id }
    };
}
