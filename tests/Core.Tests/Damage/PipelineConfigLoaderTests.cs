using System;
using System.Collections.Generic;
using System.Text.Json;
using Core.Config;
using Core.Damage;
using Core.Logging;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

public class PipelineConfigLoaderTests
{
    private readonly Mock<IResourceLoader> _mockResourceLoader;
    private readonly Mock<ILogger> _mockLogger;
    private readonly PipelineConfigLoader _loader;

    public PipelineConfigLoaderTests()
    {
        _mockResourceLoader = new Mock<IResourceLoader>();
        _mockLogger = new Mock<ILogger>();
        _loader = new PipelineConfigLoader(_mockResourceLoader.Object, _mockLogger.Object);
    }

    // ==================== CONSTRUCTOR TESTS ====================

    [Fact]
    public void Constructor_WithNullResourceLoader_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineConfigLoader(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PipelineConfigLoader(_mockResourceLoader.Object, null!));
    }

    // ==================== LOAD PIPELINE TESTS ====================

    [Fact]
    public void LoadPipeline_WithValidConfig_ReturnsConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var bucketsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                BucketId = "base",
                Order = 1,
                FilterConditions = new object[] { },
                Operations = new[]
                {
                    new
                    {
                        Type = "ADD_FLAT",
                        Source = "modifier:base_damage"
                    }
                },
                EmitEvents = true
            }
        });

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Buckets);
        Assert.Equal("base", result.Buckets[0].BucketId);
        Assert.Equal(1, result.Buckets[0].Order);
    }

    [Fact]
    public void LoadPipeline_OrdersBucketsByOrder()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var bucketsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                BucketId = "third",
                Order = 3,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            },
            new
            {
                BucketId = "first",
                Order = 1,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            },
            new
            {
                BucketId = "second",
                Order = 2,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            }
        });

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.Equal(3, result.Buckets.Count);
        Assert.Equal("first", result.Buckets[0].BucketId);
        Assert.Equal("second", result.Buckets[1].BucketId);
        Assert.Equal("third", result.Buckets[2].BucketId);
    }

    [Fact]
    public void LoadPipeline_WithMissingBucketsKey_ReturnsFallbackConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var rawData = new Dictionary<string, JsonElement>
        {
            { "invalid_key", JsonDocument.Parse("[]").RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("fallback", result.ConfigName);
        Assert.Single(result.Buckets);
        Assert.Equal("base", result.Buckets[0].BucketId);
    }

    [Fact]
    public void LoadPipeline_WithInvalidJson_ReturnsFallbackConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        
        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Throws(new JsonException("Invalid JSON"));

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("fallback", result.ConfigName);
        Assert.Single(result.Buckets);
    }

    [Fact]
    public void LoadPipeline_WithResourceLoaderException_ReturnsFallbackConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        
        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Throws(new InvalidOperationException("Resource not found"));

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("fallback", result.ConfigName);
        _mockLogger.Verify(
            l => l.LogError(It.Is<string>(s => s.Contains("Failed to load pipeline"))),
            Times.Once);
    }

    [Fact]
    public void LoadPipeline_WithDuplicateBucketIds_ReturnsFallbackConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var bucketsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                BucketId = "duplicate",
                Order = 1,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            },
            new
            {
                BucketId = "duplicate",
                Order = 2,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            }
        });

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("fallback", result.ConfigName);
        _mockLogger.Verify(
            l => l.LogError(It.Is<string>(s => s.Contains("Invalid pipeline config"))),
            Times.Once);
    }

    [Fact]
    public void LoadPipeline_LogsInformationMessages()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var bucketsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                BucketId = "base",
                Order = 1,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            }
        });

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        _loader.LoadPipeline(configChain);

        // Assert
        _mockLogger.Verify(
            l => l.LogInformation("Loading damage pipeline configuration"),
            Times.Once);
        _mockLogger.Verify(
            l => l.LogInformation(It.Is<string>(s => s.Contains("Loaded pipeline"))),
            Times.Once);
    }

    [Fact]
    public void LoadPipeline_WithEmptyBucketsArray_ReturnsFallbackConfiguration()
    {
        // Arrange
        var configChain = new List<string> { "default" };
        var bucketsJson = JsonSerializer.Serialize(Array.Empty<object>());

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        var result = _loader.LoadPipeline(configChain);

        // Assert - Empty buckets fail validation, so fallback is returned
        Assert.NotNull(result);
        Assert.Equal("fallback", result.ConfigName);
        Assert.Single(result.Buckets);
    }

    [Fact]
    public void LoadPipeline_CallsResourceLoaderWithCorrectParameters()
    {
        // Arrange
        var configChain = new List<string> { "custom", "default" };
        var bucketsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                BucketId = "base",
                Order = 1,
                FilterConditions = new object[] { },
                Operations = new object[] { },
                EmitEvents = true
            }
        });

        var rawData = new Dictionary<string, JsonElement>
        {
            { "buckets", JsonDocument.Parse(bucketsJson).RootElement }
        };

        _mockResourceLoader
            .Setup(r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false))
            .Returns(rawData);

        // Act
        _loader.LoadPipeline(configChain);

        // Assert
        _mockResourceLoader.Verify(
            r => r.LoadResource("Pipelines/DamagePipeline.json", configChain, false),
            Times.Once);
    }
}
