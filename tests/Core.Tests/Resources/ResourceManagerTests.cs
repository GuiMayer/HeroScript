using Core.Common;
using Core.Config;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Resources;

/// <summary>
/// Testes unitários para ResourceManager
/// Foca em testar métodos públicos e validação, não o carregamento interno
/// </summary>
public class ResourceManagerTests
{
    private readonly Mock<IConfigManager> _mockConfigManager;
    private readonly Mock<IResourceLoader> _mockResourceLoader;
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IResourceRegenerationProcessor> _mockRegenerationProcessor;
    private readonly ResourceManager _resourceManager;

    public ResourceManagerTests()
    {
        _mockConfigManager = new Mock<IConfigManager>();
        _mockResourceLoader = new Mock<IResourceLoader>();
        _mockLogger = new Mock<ILogger>();
        _mockRegenerationProcessor = new Mock<IResourceRegenerationProcessor>();
        _resourceManager = new ResourceManager(_mockConfigManager.Object, _mockResourceLoader.Object, _mockLogger.Object, _mockRegenerationProcessor.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConfigManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ResourceManager(null!, _mockResourceLoader.Object, _mockLogger.Object, _mockRegenerationProcessor.Object));
    }

    [Fact]
    public void Constructor_WithNullResourceLoader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ResourceManager(_mockConfigManager.Object, null!, _mockLogger.Object, _mockRegenerationProcessor.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ResourceManager(_mockConfigManager.Object, _mockResourceLoader.Object, null!, _mockRegenerationProcessor.Object));
    }
    
    [Fact]
    public void Constructor_WithNullRegenerationProcessor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ResourceManager(_mockConfigManager.Object, _mockResourceLoader.Object, _mockLogger.Object, null!));
    }

    #endregion

    #region GetDefinition Tests

    [Fact]
    public void GetDefinition_WithEmptyId_ReturnsFailure()
    {
        // Act
        var result = _resourceManager.GetDefinition("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }

    [Fact]
    public void GetDefinition_WithNullId_ReturnsFailure()
    {
        // Act
        var result = _resourceManager.GetDefinition(null!);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetDefinition_WithNonExistentId_ReturnsFailure()
    {
        // Act
        var result = _resourceManager.GetDefinition("nonexistent_resource");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    #endregion

    #region GetAllDefinitions Tests

    [Fact]
    public void GetAllDefinitions_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var results = _resourceManager.GetAllDefinitions();

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GetDefinitionsByCategory Tests

    [Fact]
    public void GetDefinitionsByCategory_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var results = _resourceManager.GetDefinitionsByCategory(ResourceCategory.VITAL);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByCategory_WithAllCategories_ReturnsEmptyBeforeLoad()
    {
        // Act & Assert
        Assert.Empty(_resourceManager.GetDefinitionsByCategory(ResourceCategory.VITAL));
        Assert.Empty(_resourceManager.GetDefinitionsByCategory(ResourceCategory.TACTICAL));
        Assert.Empty(_resourceManager.GetDefinitionsByCategory(ResourceCategory.SPECIAL));
        Assert.Empty(_resourceManager.GetDefinitionsByCategory(ResourceCategory.TEMPORARY));
    }

    #endregion

    #region GetDefinitionsByTag Tests

    [Fact]
    public void GetDefinitionsByTag_WithEmptyTag_ReturnsEmpty()
    {
        // Act
        var results = _resourceManager.GetDefinitionsByTag("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByTag_WithNullTag_ReturnsEmpty()
    {
        // Act
        var results = _resourceManager.GetDefinitionsByTag(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByTag_BeforeLoad_ReturnsEmpty()
    {
        // Act
        var results = _resourceManager.GetDefinitionsByTag("vital");

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region CreatePool Tests

    [Fact]
    public void CreatePool_WithNonExistentResource_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            _resourceManager.CreatePool("nonexistent_resource"));
    }

    [Fact]
    public void CreatePool_WithEmptyResourceId_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            _resourceManager.CreatePool(""));
    }

    [Fact]
    public void CreatePool_WithNullResourceId_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            _resourceManager.CreatePool(null!));
    }

    [Fact]
    public void CreatePoolFromDefinition_AppliesEntitySpecificMaximumCentrally()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "charge",
            DisplayName = "Charge",
            DefaultMin = 2,
            DefaultMax = 10,
            DefaultCurrent = 5
        };

        var pool = ResourcePool.Materialize(
            definition,
            initialCurrent: 12,
            maximum: 20);

        Assert.Equal(2, pool.Minimum);
        Assert.Equal(20, pool.Maximum);
        Assert.Equal(12, pool.Current);
        Assert.Same(definition, pool.Definition);
    }

    [Fact]
    public void CreatePoolFromDefinition_RejectsMaximumBelowConfiguredMinimum()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "charge",
            DisplayName = "Charge",
            DefaultMin = 2,
            DefaultMax = 10,
            DefaultCurrent = 5
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResourcePool.Materialize(
                definition,
                initialCurrent: 2,
                maximum: 1));

        Assert.Contains("configured minimum", exception.Message);
    }

    #endregion

    #region ValidateResourceDefinition Tests

    [Fact]
    public void ValidateResourceDefinition_WithValidResource_ReturnsSuccess()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "test_resource",
            DisplayName = "Test Resource",
            ShortName = "TR",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 50,
            CanBeNegative = false,
            Tags = new List<string> { "test" }
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateResourceDefinition_WithMissingResourceId_ReturnsFailure()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "",
            DisplayName = "Test",
            ShortName = "T",
            Category = ResourceCategory.TACTICAL,
            DefaultMax = 100,
            Tags = new List<string>()
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Resource ID", result.Error);
    }

    [Fact]
    public void ValidateResourceDefinition_WithMissingDisplayName_ReturnsFailure()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "test",
            DisplayName = "",
            ShortName = "T",
            Category = ResourceCategory.TACTICAL,
            DefaultMax = 100,
            Tags = new List<string>()
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Display name", result.Error);
    }

    [Fact]
    public void ValidateResourceDefinition_WithInvalidMinMax_ReturnsFailure()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "test",
            DisplayName = "Test",
            ShortName = "T",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 100,
            DefaultMax = 50,
            Tags = new List<string>()
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("max cannot be less than", result.Error);
    }

    [Fact]
    public void ValidateResourceDefinition_WithCurrentBelowMin_ReturnsFailure()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "test",
            DisplayName = "Test",
            ShortName = "T",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 10,
            DefaultMax = 100,
            DefaultCurrent = 5,
            CanBeNegative = false,
            Tags = new List<string>()
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("current cannot be less than", result.Error);
    }

    [Fact]
    public void ValidateResourceDefinition_WithCurrentAboveMax_ReturnsFailure()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "test",
            DisplayName = "Test",
            ShortName = "T",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 150,
            CanExceedMax = false,
            Tags = new List<string>()
        };

        // Act
        var result = _resourceManager.ValidateResourceDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("current cannot exceed", result.Error);
    }

    #endregion

    #region LoadResourceDefinitions Tests

    [Fact]
    public void LoadResourceDefinitions_WithInvalidConfig_HandlesGracefully()
    {
        // Arrange
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
            .Throws(new Exception("Config not found"));

        // Act
        _resourceManager.LoadResourceDefinitions("invalid_config");
        var allResources = _resourceManager.GetAllDefinitions();

        // Assert
        Assert.Empty(allResources);
        _mockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void LoadResourceDefinitions_WithEmptyConfig_ReturnsEmptyList()
    {
        // Arrange
        var configChain = new List<string> { "empty_config" };
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
            .Returns(configChain);
        _mockResourceLoader.Setup(m => m.LoadResource(It.IsAny<string>(), It.IsAny<List<string>>(), false))
            .Returns(new Dictionary<string, System.Text.Json.JsonElement>());

        // Act
        _resourceManager.LoadResourceDefinitions("empty_config");
        var allResources = _resourceManager.GetAllDefinitions();

        // Assert
        Assert.Empty(allResources);
    }

    #endregion
}
