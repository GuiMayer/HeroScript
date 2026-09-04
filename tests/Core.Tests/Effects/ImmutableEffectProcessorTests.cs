using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.Effects;

public sealed class ImmutableEffectProcessorTests
{
    private readonly ImmutableEffectProcessor _processor = new();

    [Fact]
    public void Damage_ReducesTheExplicitArbitraryResource()
    {
        var state = State(Entity("hero", true, ("mana", 10)), Entity("enemy", false, ("mana", 8)));
        var effect = ResourceEffect(
            "mana-burn",
            EffectType.DAMAGE,
            "mana",
            3,
            EffectProvenanceKind.Card);

        var result = _processor.Apply(state, [effect]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(5, result.Value.State.GetEntity("enemy")!.GetResource("mana")!.Current);
        Assert.Equal(8, state.GetEntity("enemy")!.GetResource("mana")!.Current);
        Assert.Equal("mana", Assert.Single(result.Value.Records).ResourceId);
    }

    [Fact]
    public void ProvenanceDoesNotChangeResourceExecution()
    {
        var state = State(Entity("hero", true, ("focus", 10)), Entity("enemy", false, ("focus", 8)));
        var card = _processor.Apply(state,
        [
            ResourceEffect("same", EffectType.DAMAGE, "focus", 3, EffectProvenanceKind.Card)
        ]);
        var status = _processor.Apply(state,
        [
            ResourceEffect("same", EffectType.DAMAGE, "focus", 3, EffectProvenanceKind.Status)
        ]);

        Assert.True(card.IsSuccess && status.IsSuccess);
        Assert.Equal(
            card.Value.State.GetEntity("enemy")!.GetResource("focus")!.Current,
            status.Value.State.GetEntity("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(EffectProvenanceKind.Card, card.Value.Records[0].Provenance.Kind);
        Assert.Equal(EffectProvenanceKind.Status, status.Value.Records[0].Provenance.Kind);
    }

    [Fact]
    public void Apply_IsAtomicWhenAnyEffectIsInvalid()
    {
        var state = State(Entity("hero", true, ("mana", 10)), Entity("enemy", false, ("mana", 8)));

        var result = _processor.Apply(state,
        [
            ResourceEffect("valid", EffectType.DAMAGE, "mana", 3, EffectProvenanceKind.Card),
            ResourceEffect("invalid", EffectType.DAMAGE, "missing", 2, EffectProvenanceKind.Relic)
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal(8, state.GetEntity("enemy")!.GetResource("mana")!.Current);
    }

    [Fact]
    public void StatusApplication_UpdatesOnlyImmutableCombatSnapshot()
    {
        var state = State(Entity("hero", true, ("mana", 10)), Entity("enemy", false, ("mana", 8)));
        var definition = new StatusEffectDefinition
        {
            StatusId = "burning",
            DefaultStacks = 1,
            MaxStacks = 10,
            DefaultDuration = 3
        };
        var effect = new ResolvedEffectCommand
        {
            EffectInstanceId = "apply-burning",
            Definition = new EffectDefinition
            {
                Type = EffectType.APPLY_STATUS,
                StatusId = "burning",
                StatusStacks = 2
            },
            SourceEntityId = "hero",
            TargetEntityIds = ["enemy"],
            StatusDefinition = definition,
            Provenance = new EffectProvenance
            {
                Kind = EffectProvenanceKind.Card,
                SourceId = "card-instance"
            }
        };

        var result = _processor.Apply(state, [effect]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(state.StatusEffects);
        var status = Assert.Single(result.Value.State.StatusEffects["enemy"]);
        Assert.Equal("burning", status.StatusId);
        Assert.Equal(2, status.Stacks);
        Assert.NotEqual(Guid.Empty, status.InstanceId);
    }

    private static ResolvedEffectCommand ResourceEffect(
        string id,
        EffectType type,
        string resourceId,
        float value,
        EffectProvenanceKind provenance) => new()
    {
        EffectInstanceId = id,
        Definition = new EffectDefinition
        {
            EffectId = id,
            Type = type,
            TargetResource = resourceId
        },
        SourceEntityId = "hero",
        TargetEntityIds = ["enemy"],
        ResolvedValue = value,
        Provenance = new EffectProvenance
        {
            Kind = provenance,
            SourceId = "source"
        }
    };

    private static CombatState State(CombatEntity hero, params CombatEntity[] enemies) => new()
    {
        CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001"),
        Hero = hero,
        Enemies = enemies,
        Determinism = DeterministicContext.Create(44, "content-v1")
    };

    private static CombatEntity Entity(
        string id,
        bool isHero,
        params (string Id, float Current)[] resources) => new()
    {
        EntityId = id,
        IsHero = isHero,
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = resources.ToDictionary(
                item => item.Id,
                item => new ResourcePool
                {
                    ResourceId = item.Id,
                    Current = item.Current,
                    Minimum = 0,
                    Maximum = 100,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = item.Id,
                        DisplayName = item.Id
                    }
                },
                StringComparer.Ordinal)
        }
    };
}
