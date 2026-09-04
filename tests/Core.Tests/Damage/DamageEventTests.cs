using Core.Combat.Models;
using Core.Damage;
using Core.Damage.Events;
using Core.Effects;
using Core.Events;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

public class DamageEventTests
{
    [Fact]
    public void CalculateDamage_PublishesDamageCalculatedEvent_WithExpectedPayload()
    {
        DamageCalculatedEvent? publishedEvent = null;
        var pipelineManager = new Mock<IPipelineManager>();
        var eventBus = new Mock<IEventBus>();
        var logger = DamageTestHelpers.CreateMockLogger();

        pipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext context) => context with
            {
                CurrentDamage = 15f,
                Metadata = new Dictionary<string, object>(context.Metadata)
                {
                    ["crit_tier"] = 1,
                    ["source"] = "test_pipeline"
                }
            });

        eventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = Assert.IsType<DamageCalculatedEvent>(evt));

        var calculator = new DamageCalculator(pipelineManager.Object, eventBus.Object, logger.Object);
        var action = new ActionDefinition
        {
            ActionId = "fireball",
            Tags = new List<string> { "fire", "spell" },
            Effects = new List<EffectDefinition>
            {
                new()
                {
                    Type = EffectType.DAMAGE,
                    FlatValue = 10f,
                    Target = EffectTarget.TARGET
                }
            }
        };
        var attacker = CreateEntity("hero", isHero: true);
        var target = CreateEntity("enemy", isHero: false, armor: 2f);

        var result = calculator.CalculateDamage(action, attacker, target);

        Assert.Equal(15f, result.FinalDamage);
        Assert.Equal(1, result.CritTier);
        Assert.NotNull(publishedEvent);
        Assert.Equal("fireball", publishedEvent.ActionId);
        Assert.Equal("hero", publishedEvent.AttackerId);
        Assert.Equal("enemy", publishedEvent.TargetId);
        Assert.Equal(10f, publishedEvent.BaseDamage);
        Assert.Equal(15f, publishedEvent.FinalDamage);
        Assert.Equal(1, publishedEvent.CritTier);
        Assert.Contains("fire", publishedEvent.Tags);
        Assert.Equal("test_pipeline", publishedEvent.Metadata["source"]);
    }

    [Fact]
    public void CalculateDamage_WhenPipelineReturnsNegativeDamage_EventFinalDamageIsClampedToZero()
    {
        DamageCalculatedEvent? publishedEvent = null;
        var pipelineManager = new Mock<IPipelineManager>();
        var eventBus = new Mock<IEventBus>();
        var logger = DamageTestHelpers.CreateMockLogger();

        pipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext context) => context with { CurrentDamage = -50f });

        eventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = Assert.IsType<DamageCalculatedEvent>(evt));

        var calculator = new DamageCalculator(pipelineManager.Object, eventBus.Object, logger.Object);
        var action = new ActionDefinition
        {
            ActionId = "shield_bash",
            Effects = new List<EffectDefinition>
            {
                new()
                {
                    Type = EffectType.DAMAGE,
                    FlatValue = 5f,
                    Target = EffectTarget.TARGET
                }
            }
        };

        var result = calculator.CalculateDamage(action, CreateEntity("hero", true), CreateEntity("enemy", false));

        Assert.Equal(0f, result.FinalDamage);
        Assert.NotNull(publishedEvent);
        Assert.Equal(0f, publishedEvent.FinalDamage);
        Assert.Equal(5f, publishedEvent.BaseDamage);
    }

    private static CombatEntity CreateEntity(string id, bool isHero, float armor = 0f)
    {
        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = new()
            {
                ResourceId = "health",
                Current = 100f,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "health",
                    DisplayName = "Health",
                    Category = ResourceCategory.VITAL
                }
            },
            ["armor"] = new()
            {
                ResourceId = "armor",
                Current = armor,
                Maximum = 999f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "armor",
                    DisplayName = "Armor",
                    Category = ResourceCategory.TEMPORARY
                }
            }
        };

        return new CombatEntity
        {
            EntityId = id,
            Name = id,
            IsHero = isHero,
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = resources
            }
        };
    }
}
