using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Core.Config;
using Core.Damage;
using Core.Damage.Events;
using Core.Events;
using Core.Logging;
using Core.Math;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

public class PipelineManagerTests
{
    private readonly Mock<IResourceLoader> _mockResourceLoader;
    private readonly Mock<IConfigManager> _mockConfigManager;
    private readonly Mock<IMathEngine> _mockMathEngine;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<ILogger> _mockLoaderLogger;
    private readonly Mock<IRandomProvider> _mockRandomProvider;

    public PipelineManagerTests()
    {
        _mockResourceLoader = new Mock<IResourceLoader>();
        _mockConfigManager = new Mock<IConfigManager>();
        _mockConfigManager.SetupGet(c => c.DefaultConfig).Returns("test_config");
        _mockConfigManager.Setup(c => c.ResolveInheritanceChain("test_config")).Returns(new[] { "test_config" });
        _mockMathEngine = new Mock<IMathEngine>();
        _mockEventBus = new Mock<IEventBus>();
        _mockLogger = new Mock<ILogger>();
        _mockLoaderLogger = new Mock<ILogger>();
        _mockRandomProvider = new Mock<IRandomProvider>();
    }

    private PipelineManager CreateManager()
    {
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);
        return new PipelineManager(loader, _mockConfigManager.Object, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);
    }

    private void SetupResourceLoaderWithConfig(PipelineConfiguration config)
    {
        // Use lowercase "buckets" to match PipelineConfigLoader expectation
        var json = JsonSerializer.Serialize(new { buckets = config.Buckets });
        var jsonDoc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, JsonElement>();
        
        foreach (var property in jsonDoc.RootElement.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }
        
        _mockResourceLoader.Setup(r => r.LoadResource(
            "Pipelines/DamagePipeline.json",
            It.IsAny<IEnumerable<string>>(),
            false))
            .Returns(dict);
    }

    // ==================== CONSTRUCTOR VALIDATION ====================

    [Fact]
    public void Constructor_WithNullLoader_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineManager(null!, _mockConfigManager.Object, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfigManager_ThrowsArgumentNullException()
    {
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);

        Assert.Throws<ArgumentNullException>(() =>
            new PipelineManager(loader, null!, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMathEngine_ThrowsArgumentNullException()
    {
        // Arrange
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineManager(loader, _mockConfigManager.Object, null!, _mockEventBus.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventBus_ThrowsArgumentNullException()
    {
        // Arrange
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineManager(loader, _mockConfigManager.Object, _mockMathEngine.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineManager(loader, _mockConfigManager.Object, _mockMathEngine.Object, _mockEventBus.Object, null!));
    }

    [Fact]
    public void Constructor_WithNullRandomProvider_UsesDefaultRandomProvider()
    {
        // Arrange
        var loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLoaderLogger.Object);
        
        // Act - não deve lançar exceção
        var manager = new PipelineManager(
            loader, 
            _mockConfigManager.Object,
            _mockMathEngine.Object, 
            _mockEventBus.Object, 
            _mockLogger.Object, 
            null);

        // Assert - se chegou aqui, o construtor aceitou null e usou o default
        Assert.NotNull(manager);
    }

    // ==================== EXECUTE PIPELINE ====================

    [Fact]
    public void ExecutePipeline_WithValidContext_ProcessesThroughAllBuckets()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "bucket1",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:10" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "bucket2",
                    Order = 2,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:2" }
                    }
                }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert
        // (100 + 10) * 2 = 220
        Assert.Equal(220f, result.CurrentDamage, precision: 2);
    }

    [Fact]
    public void ExecutePipeline_LogsStartAndEnd()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "bucket1",
                    Order = 1,
                    Operations = new List<BucketOperation>()
                }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        manager.ExecutePipeline(context);

        // Assert
        _mockLogger.Verify(l => l.LogDebug(It.Is<string>(s => s.Contains("Pipeline start"))), Times.Once);
        _mockLogger.Verify(l => l.LogDebug(It.Is<string>(s => s.Contains("Pipeline end"))), Times.Once);
    }

    [Fact]
    public void ExecutePipeline_WhenBucketThrows_DoesNotContinueToLaterBuckets()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "failing_bucket",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = (OperationType)999, Source = "invalid" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "later_bucket",
                    Order = 2,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:999" }
                    }
                }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => manager.ExecutePipeline(context));

        // Assert
        Assert.Contains("failing_bucket", exception.Message);
        _mockEventBus.Verify(e => e.Publish(It.Is<BucketProcessedEvent>(evt => evt.BucketId == "later_bucket")), Times.Never);
        _mockLogger.Verify(l => l.LogError(It.Is<string>(s => s.Contains("failing_bucket")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ExecutePipeline_WhenBucketThrows_DoesNotMutateOriginalContext()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "first_bucket",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:25" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "failing_bucket",
                    Order = 2,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = (OperationType)999, Source = "invalid" }
                    }
                }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        Assert.Throws<InvalidOperationException>(() => manager.ExecutePipeline(context));

        // Assert
        Assert.Equal(100f, context.CurrentDamage);
        Assert.Empty(context.Metadata);
        Assert.Empty(context.Tags);
    }

    // ==================== RELOAD CONFIGURATION ====================

    [Fact]
    public void ReloadConfiguration_WithValidConfig_LoadsSuccessfully()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "test", Order = 1, Operations = new List<BucketOperation>() }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();

        // Act
        manager.ReloadConfiguration(new[] { "test_config" });

        // Assert
        _mockResourceLoader.Verify(r => r.LoadResource(
            "Pipelines/DamagePipeline.json",
            It.Is<IEnumerable<string>>(c => c.Contains("test_config")),
            false), Times.Once);
        _mockLogger.Verify(l => l.LogInformation(It.Is<string>(s => s.Contains("Reloading pipeline"))), Times.Once);
        _mockLogger.Verify(l => l.LogInformation(It.Is<string>(s => s.Contains("Pipeline reloaded"))), Times.Once);
    }

    [Fact]
    public void ReloadConfiguration_WithValidConfig_EmitsSuccessEvent()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "bucket1", Order = 1, Operations = new List<BucketOperation>() },
                new BucketDefinition { BucketId = "bucket2", Order = 2, Operations = new List<BucketOperation>() }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();

        // Act
        manager.ReloadConfiguration(new[] { "test_config" });

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<PipelineReloadedEvent>(evt => 
            evt.Success == true &&
            evt.BucketCount == 2 &&
            evt.BucketIds.Contains("bucket1") &&
            evt.BucketIds.Contains("bucket2") &&
            evt.Reason == "Manual reload"
        )), Times.Once);
    }

    [Fact]
    public void ReloadConfiguration_WithLoaderException_EmitsFailureEventAndThrows()
    {
        // Arrange
        _mockResourceLoader.Setup(r => r.LoadResource(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Config load failed"));

        var manager = CreateManager();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => manager.ReloadConfiguration(new[] { "bad_config" }));

        _mockEventBus.Verify(e => e.Publish(It.Is<PipelineReloadedEvent>(evt =>
            evt.Success == false &&
            evt.ErrorMessage == "Config load failed"
        )), Times.Once);
        _mockLogger.Verify(l => l.LogError(It.Is<string>(s => s.Contains("Pipeline reload failed"))), Times.Once);
    }

    [Fact]
    public void ReloadConfiguration_WithLoaderException_DoesNotCacheConfiguration()
    {
        // Arrange
        _mockResourceLoader.Setup(r => r.LoadResource(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Config load failed"));

        var manager = CreateManager();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => manager.ReloadConfiguration(new[] { "bad_config" }));

        SetupResourceLoaderWithConfig(new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "recovered", Order = 1, Operations = new List<BucketOperation>() }
            }
        });

        var config = manager.GetCurrentConfiguration();
        Assert.Equal("recovered", config.Buckets.Single().BucketId);
    }

    // ==================== GET CURRENT CONFIGURATION ====================

    [Fact]
    public void GetCurrentConfiguration_WhenNotLoaded_TriggersLazyLoad()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "lazy", Order = 1, Operations = new List<BucketOperation>() }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();

        // Act
        var result = manager.GetCurrentConfiguration();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Buckets);
        Assert.Equal("lazy", result.Buckets[0].BucketId);
        _mockResourceLoader.Verify(r => r.LoadResource(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void GetCurrentConfiguration_WhenAlreadyLoaded_ReturnsCachedConfig()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "cached", Order = 1, Operations = new List<BucketOperation>() }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();

        // Act
        var result1 = manager.GetCurrentConfiguration();
        var result2 = manager.GetCurrentConfiguration();

        // Assert
        Assert.Same(result1, result2); // Mesma instância
        _mockResourceLoader.Verify(r => r.LoadResource(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()), Times.Once); // Carregou apenas uma vez
    }

    // ==================== LAZY LOADING ====================

    [Fact]
    public void ExecutePipeline_OnFirstCall_TriggersLazyLoad()
    {
        // Arrange
        var config = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition { BucketId = "lazy_exec", Order = 1, Operations = new List<BucketOperation>() }
            }
        };

        SetupResourceLoaderWithConfig(config);
        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        manager.ExecutePipeline(context);

        // Assert
        _mockResourceLoader.Verify(r => r.LoadResource(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void ExecutePipeline_AfterReload_UsesNewConfiguration()
    {
        // Arrange
        var config1 = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "old",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:10" }
                    }
                }
            }
        };

        var config2 = new PipelineConfiguration
        {
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "new",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:50" }
                    }
                }
            }
        };

        // Use lowercase "buckets" to match PipelineConfigLoader expectation
        var json1 = JsonSerializer.Serialize(new { buckets = config1.Buckets });
        var json2 = JsonSerializer.Serialize(new { buckets = config2.Buckets });

        var jsonDoc1 = JsonDocument.Parse(json1);
        var dict1 = new Dictionary<string, JsonElement>();
        foreach (var property in jsonDoc1.RootElement.EnumerateObject())
        {
            dict1[property.Name] = property.Value.Clone();
        }

        var jsonDoc2 = JsonDocument.Parse(json2);
        var dict2 = new Dictionary<string, JsonElement>();
        foreach (var property in jsonDoc2.RootElement.EnumerateObject())
        {
            dict2[property.Name] = property.Value.Clone();
        }

        _mockResourceLoader.SetupSequence(r => r.LoadResource(
            "Pipelines/DamagePipeline.json",
            It.IsAny<IEnumerable<string>>(),
            false))
            .Returns(dict1)
            .Returns(dict2);

        var manager = CreateManager();
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result1 = manager.ExecutePipeline(context);
        manager.ReloadConfiguration(new[] { "new_config" });
        var result2 = manager.ExecutePipeline(context);

        // Assert
        Assert.Equal(110f, result1.CurrentDamage, precision: 2); // 100 + 10
        Assert.Equal(150f, result2.CurrentDamage, precision: 2); // 100 + 50
    }
}
