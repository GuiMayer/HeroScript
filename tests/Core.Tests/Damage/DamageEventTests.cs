using System;
using System.Collections.Generic;
using Core.Combat;
using Core.Damage;
using Core.Damage.Events;
using Core.Events;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes para verificar que eventos são emitidos corretamente pelo sistema de dano.
/// </summary>
public class DamageEventTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IMathEngine> _mockMathEngine;
    private readonly Mock<IPipelineManager> _mockPipelineManager;

    public DamageEventTests()
    {
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockEventBus = DamageTestHelpers.CreateMockEventBus();
        _mockMathEngine = DamageTestHelpers.CreateMockMathEngine();
        _mockPipelineManager = new Mock<IPipelineManager>();
    }

    private CombatEntity CreateTestEntity(string entityId)
    {
        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = new ResourcePool
            {
                ResourceId = "health",
                Current = 100f,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "health", DisplayName = "Health" }
            },
            ["crit_chance"] = new ResourcePool
            {
                ResourceId = "crit_chance",
                Current = 5f,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "crit_chance", DisplayName = "Crit Chance" }
            },
            ["crit_multiplier"] = new ResourcePool
            {
                ResourceId = "crit_multiplier",
                Current = 2.0f,
                Maximum = 10f,
                Minimum = 1f,
                Definition = new ResourceDefinition { ResourceId = "crit_multiplier", DisplayName = "Crit Multiplier" }
            },
            ["armor"] = new ResourcePool
            {
                ResourceId = "armor",
                Current = 10f,
                Maximum = 1000f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "armor", DisplayName = "Armor" }
            }
        };

        return new CombatEntity
        {
            EntityId = entityId,
            Name = entityId,
            ResourceState = new EntityResourceState
            {
                EntityId = entityId,
                Resources = resources
            }
        };
    }

    // ==================== BUCKET PROCESSED EVENT ====================

    [Fact]
    public void BucketProcessor_EmitsBucketProcessedEvent()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        processor.Process(context);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.BucketId == "test_bucket" &&
            evt.DamageBefore == 100f &&
            evt.DamageAfter == 200f &&
            evt.DamageDelta == 100f
        )), Times.Once);
    }

    [Fact]
    public void BucketProcessor_EventContainsMetadata()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "metadata_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:50"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);
        context.Metadata["test_key"] = "test_value";

        // Act
        processor.Process(context);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.Metadata.ContainsKey("test_key") &&
            evt.Metadata["test_key"].ToString() == "test_value"
        )), Times.Once);
    }

    [Fact]
    public void BucketProcessor_WithNegativeDelta_EmitsCorrectEvent()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "negative_delta_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:-30"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        processor.Process(context);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.DamageBefore == 100f &&
            evt.DamageAfter == 70f &&
            evt.DamageDelta == -30f
        )), Times.Once);
    }

    // ==================== DAMAGE CALCULATED EVENT ====================

    [Fact]
    public void DamageCalculator_EmitsDamageCalculatedEvent()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            BaseDamage = 100f,
            Tags = new List<string> { "physical", "melee" }
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 150f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = ctx.Metadata
            });

        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.ActionId == "test_action" &&
            evt.AttackerId == "player1" &&
            evt.TargetId == "enemy1" &&
            evt.BaseDamage == 100f &&
            evt.FinalDamage == 150f &&
            evt.Tags.Contains("physical") &&
            evt.Tags.Contains("melee")
        )), Times.Once);
    }

    [Fact]
    public void DamageCalculator_EventContainsCritTier()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "crit_action",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 250f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = new Dictionary<string, object>
                {
                    ["crit_tier"] = 2
                }
            });

        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.CritTier == 2 &&
            evt.FinalDamage == 250f
        )), Times.Once);
    }

    [Fact]
    public void DamageCalculator_EventContainsMetadata()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "metadata_action",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        var customMetadata = new Dictionary<string, object>
        {
            ["custom_key"] = "custom_value",
            ["damage_type"] = "fire"
        };

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 150f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = customMetadata
            });

        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.Metadata.ContainsKey("custom_key") &&
            evt.Metadata["custom_key"].ToString() == "custom_value" &&
            evt.Metadata.ContainsKey("damage_type") &&
            evt.Metadata["damage_type"].ToString() == "fire"
        )), Times.Once);
    }

    // ==================== MULTIPLE BUCKETS ====================

    [Fact]
    public void MultipleBuckets_EmitMultipleBucketProcessedEvents()
    {
        // Arrange
        var bucket1 = new BucketDefinition
        {
            BucketId = "bucket1",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:2" }
            }
        };

        var bucket2 = new BucketDefinition
        {
            BucketId = "bucket2",
            Order = 2,
            Operations = new List<BucketOperation>
            {
                new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:50" }
            }
        };

        var processor1 = new GenericBucketProcessor(bucket1, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var processor2 = new GenericBucketProcessor(bucket2, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result1 = processor1.Process(context);
        var result2 = processor2.Process(result1);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.BucketId == "bucket1" &&
            evt.DamageBefore == 100f &&
            evt.DamageAfter == 200f
        )), Times.Once);

        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.BucketId == "bucket2" &&
            evt.DamageBefore == 200f &&
            evt.DamageAfter == 250f
        )), Times.Once);
    }

    // ==================== EVENT TIMING ====================

    [Fact]
    public void BucketProcessedEvent_HasRecentTimestamp()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "timestamp_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        var beforeTime = DateTime.UtcNow;

        // Act
        processor.Process(context);

        var afterTime = DateTime.UtcNow;

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt =>
            evt.Timestamp >= beforeTime &&
            evt.Timestamp <= afterTime
        )), Times.Once);
    }

    [Fact]
    public void DamageCalculatedEvent_HasRecentTimestamp()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "timestamp_action",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => ctx);

        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);

        var beforeTime = DateTime.UtcNow;

        // Act
        calculator.CalculateDamage(action, attacker, target);

        var afterTime = DateTime.UtcNow;

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.Timestamp >= beforeTime &&
            evt.Timestamp <= afterTime
        )), Times.Once);
    }

    // ==================== EVENT UNIQUENESS ====================

    [Fact]
    public void BucketProcessedEvent_HasUniqueEventId()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "unique_id_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        var capturedEventIds = new List<Guid>();

        _mockEventBus.Setup(e => e.Publish(It.IsAny<BucketProcessedEvent>()))
            .Callback<IEvent>(evt => capturedEventIds.Add(evt.EventId));

        // Act
        processor.Process(context);
        processor.Process(context);

        // Assert
        Assert.Equal(2, capturedEventIds.Count);
        Assert.NotEqual(capturedEventIds[0], capturedEventIds[1]);
    }

    [Fact]
    public void DamageCalculatedEvent_HasCorrectEventType()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "event_type_test",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => ctx);

        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.EventType == "DamageCalculatedEvent"
        )), Times.Once);
    }
}
