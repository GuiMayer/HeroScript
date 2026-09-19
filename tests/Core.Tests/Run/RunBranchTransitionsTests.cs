using Core.Determinism;
using Core.Combat.Models;
using Core.Combat.Activation;
using Core.Combat.Modifiers;
using Core.Resources;
using Core.Run;
using Core.Run.Branching;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunBranchTransitionsTests
{
    [Fact]
    public void Create_IsReproducibleAndLeavesSourceUntouched()
    {
        var source = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000010"),
            Sequence = 7,
            PlayerEntityId = "player",
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 25 }),
            Determinism = DeterministicContext.Create(123, "content-v1").AdvanceStep()
        };
        source = source with { Lineage = RunLineage.Root(source.RunId) };
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "alternate",
            CanonicalJson.ComputeHash(source));

        var first = RunBranchTransitions.Create(source, command).Value;
        var second = RunBranchTransitions.Create(source, command).Value;

        Assert.Equal(first, second);
        Assert.Equal(7, source.Sequence);
        Assert.Null(source.Lineage!.ParentRunId);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(source.RunId, first.Lineage!.ParentRunId);
        Assert.Equal(source.Sequence, first.Lineage.SourceSequence);
        Assert.NotEqual(source.RunId, first.RunId);
        Assert.True(first.Determinism.Step > source.Determinism.Step);
    }

    [Fact]
    public void Create_CopiesActiveEncounterIntoIndependentCombatBranch()
    {
        var combatId = Guid.Parse("20000000-0000-0000-0000-000000000011");
        var source = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000011"),
            Sequence = 2,
            ActiveEncounterId = combatId,
            Encounters =
            [
                new RunEncounterState
                {
                    NodeId = "start",
                    Combat = new Core.Combat.Models.CombatState
                    {
                        CombatId = combatId,
                        RunId = Guid.Parse("10000000-0000-0000-0000-000000000011"),
                        Determinism = DeterministicContext.Create(987, "content-v1"),
                        Actors = new Dictionary<string, CombatActorState> { ["hero"] = CreateHero() }
                    }
                }
            ],
            Determinism = DeterministicContext.Create(456, "content-v1")
        };
        source = source with { Lineage = RunLineage.Root(source.RunId) };
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "alternate-combat",
            CanonicalJson.ComputeHash(source),
            combatId);

        var branch = RunBranchTransitions.Create(source, command);
        Assert.True(branch.IsSuccess, branch.IsFailure ? branch.Error : null);
        Assert.NotEqual(combatId, branch.Value.ActiveEncounterId);
        Assert.Equal(combatId, branch.Value.Lineage!.SourceCombatId);
        Assert.Equal(branch.Value.RunId, branch.Value.GetActiveEncounter()!.Combat.RunId);
        Assert.NotEqual(combatId, branch.Value.GetActiveEncounter()!.Combat.CombatId);
        Assert.True(RunBranchTransitions.Create(
            source with { ActiveEncounterId = null },
            command).IsFailure);
    }

    [Fact]
    public void Create_RebasesRunOwnershipAndPreservesExistingGameplayInstanceIds()
    {
        var runId = Guid.Parse("10000000-0000-0000-0000-000000000012");
        var combatId = Guid.Parse("20000000-0000-0000-0000-000000000012");
        var cardId = Guid.Parse("30000000-0000-8000-8000-000000000012");
        var relicId = Guid.Parse("40000000-0000-8000-8000-000000000012");
        var modifierId = Guid.Parse("50000000-0000-8000-8000-000000000012");
        var source = new RunState
        {
            RunId = runId,
            Sequence = 4,
            Lineage = RunLineage.Root(runId),
            ResourceState = TestDataBuilders.RunResources() with { OwnerId = $"run:{runId}" },
            Deck = TestCardZones.WithInstance("cards",
                new CardInstanceState { CardInstanceId = cardId, DefinitionId = "strike" }),
            Relics =
            [
                new RunRelicState
                {
                    RelicInstanceId = relicId,
                    DefinitionId = "relic",
                    Owner = new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = runId.ToString() }
                }
            ],
            Modifiers =
            [
                new ScriptModifierInstance
                {
                    InstanceId = modifierId,
                    ModifierId = "modifier",
                    OwnerId = $"run:{runId}",
                    Owner = new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = runId.ToString() }
                }
            ],
            CardSelections = [new CardSelectionState { SelectionInstanceId = Guid.NewGuid(), RunId = runId }],
            Shops = [new ShopState { ShopInstanceId = Guid.NewGuid(), RunId = runId }],
            Preparations = [new PreparationState { PreparationInstanceId = Guid.NewGuid(), RunId = runId }],
            ActiveEncounterId = combatId,
            Encounters =
            [
                new RunEncounterState
                {
                    Combat = new Core.Combat.Models.CombatState
                    {
                        CombatId = combatId,
                        RunId = runId,
                        Determinism = DeterministicContext.Create(987, "content-v1"),
                        Actors = new Dictionary<string, CombatActorState> { ["hero"] = CreateHero() },
                        ActivationState = new ActivationState { RunId = runId }
                    }
                }
            ],
            CombatResolutions = new Dictionary<Guid, CombatResolutionRecord>
            {
                [Guid.NewGuid()] = new()
            },
            Determinism = DeterministicContext.Create(456, "content-v1")
        };
        var command = new RunBranchStartCommand(
            runId,
            source.Sequence,
            "ownership-rebase",
            CanonicalJson.ComputeHash(source),
            combatId,
            runId);

        var result = RunBranchTransitions.Create(source, command);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var branch = result.Value;
        Assert.Equal($"run:{branch.RunId}", branch.ResourceState.OwnerId);
        Assert.Equal(cardId, Assert.Single(branch.Deck.Topology.Instances).Key);
        Assert.Equal(relicId, Assert.Single(branch.Relics).RelicInstanceId);
        Assert.Equal(branch.RunId.ToString(), branch.Relics[0].Owner.Id);
        Assert.Equal(modifierId, Assert.Single(branch.Modifiers).InstanceId);
        Assert.Equal($"run:{branch.RunId}", branch.Modifiers[0].OwnerId);
        Assert.Equal(branch.RunId, branch.CardSelections[0].RunId);
        Assert.Equal(branch.RunId, branch.Shops[0].RunId);
        Assert.Equal(branch.RunId, branch.Preparations[0].RunId);
        Assert.Equal(branch.RunId, branch.GetActiveEncounter()!.Combat.RunId);
        Assert.Equal(branch.RunId, branch.GetActiveEncounter()!.Combat.ActivationState!.RunId);
        Assert.Empty(branch.CombatResolutions);
        Assert.Single(source.CombatResolutions);
    }

    private static CombatActorState CreateHero() => new()
    {
        InstanceId = "hero",
        Name = "Hero",
        SideId = "player", ControllerBinding = new ControllerBinding { Kind = ControllerKind.Player },
        ResourceState = new ResourceSet
        {
            OwnerId = "hero",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new ResourcePool
                {
                    ResourceId = "health",
                    Current = 10,
                    Maximum = 10,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "health",
                        DisplayName = "Health",
                        Category = ResourceCategory.VITAL
                    }
                }
            }
        }
    };
}
