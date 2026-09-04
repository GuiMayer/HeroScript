using Core.Determinism;
using Core.Combat.Models;
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
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "alternate",
            CanonicalJson.ComputeHash(source));

        var first = RunBranchTransitions.Create(source, command).Value;
        var second = RunBranchTransitions.Create(source, command).Value;

        Assert.Equal(first, second);
        Assert.Equal(7, source.Sequence);
        Assert.Null(source.ParentRunId);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(source.RunId, first.ParentRunId);
        Assert.Equal(source.Sequence, first.BranchFromSequence);
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
                        Hero = CreateHero()
                    }
                }
            ],
            Determinism = DeterministicContext.Create(456, "content-v1")
        };
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "alternate-combat",
            CanonicalJson.ComputeHash(source),
            combatId);

        var branch = RunBranchTransitions.Create(source, command);
        Assert.True(branch.IsSuccess, branch.IsFailure ? branch.Error : null);
        Assert.NotEqual(combatId, branch.Value.ActiveEncounterId);
        Assert.Equal(combatId, branch.Value.ParentCombatId);
        Assert.Equal(branch.Value.RunId, branch.Value.GetActiveEncounter()!.Combat.RunId);
        Assert.NotEqual(combatId, branch.Value.GetActiveEncounter()!.Combat.CombatId);
        Assert.True(RunBranchTransitions.Create(
            source with { ActiveEncounterId = null },
            command).IsFailure);
    }

    private static CombatEntity CreateHero() => new()
    {
        EntityId = "hero",
        Name = "Hero",
        IsHero = true,
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
