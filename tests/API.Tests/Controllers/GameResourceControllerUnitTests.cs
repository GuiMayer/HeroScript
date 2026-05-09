using API.Controllers;
using API.Models.Resources;
using Core.Common;
using Core.Resources;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using CoreLogger = Core.Logging.ILogger;

namespace API.Tests.Controllers;

/// <summary>
/// Unit tests for GameResourceController using mocked dependencies.
/// These tests focus on controller logic, validation, and error handling.
/// </summary>
public class GameResourceControllerUnitTests
{
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly Mock<CoreLogger> _mockLogger;
    private readonly GameResourceController _controller;

    public GameResourceControllerUnitTests()
    {
        _mockResourceManager = new Mock<IResourceManager>();
        _mockLogger = new Mock<CoreLogger>();
        _controller = new GameResourceController(_mockResourceManager.Object, _mockLogger.Object);
    }

    #region GetAllResources Tests

    [Fact]
    public void GetAllResources_ReturnsOkWithResources()
    {
        // Arrange
        var resources = new List<ResourceDefinition>
        {
            new ResourceDefinition
            {
                ResourceId = "health",
                DisplayName = "Health",
                ShortName = "HP",
                Category = ResourceCategory.VITAL,
                DefaultMax = 100
            }
        };
        _mockResourceManager.Setup(m => m.GetAllDefinitions()).Returns(resources);

        // Act
        var result = _controller.GetAllResources();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Equal("health", summaries[0].ResourceId);
    }

    [Fact]
    public void GetAllResources_ReturnsEmptyList_WhenNoResources()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetAllDefinitions()).Returns(new List<ResourceDefinition>());

        // Act
        var result = _controller.GetAllResources();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    [Fact]
    public void GetAllResources_Returns500_OnException()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetAllDefinitions()).Throws(new Exception("Database error"));

        // Act
        var result = _controller.GetAllResources();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    #endregion

    #region GetResource Tests

    [Fact]
    public void GetResource_ReturnsOk_WithValidId()
    {
        // Arrange
        var resource = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            ShortName = "MP",
            Category = ResourceCategory.TACTICAL,
            DefaultMax = 100
        };
        _mockResourceManager.Setup(m => m.GetDefinition("mana"))
            .Returns(Result<ResourceDefinition>.Success(resource));

        // Act
        var result = _controller.GetResource("mana");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ResourceDefinitionDto>(okResult.Value);
        Assert.Equal("mana", dto.ResourceId);
    }

    [Fact]
    public void GetResource_Returns404_WithInvalidId()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetDefinition("invalid"))
            .Returns(Result<ResourceDefinition>.Failure("Resource not found: invalid"));

        // Act
        var result = _controller.GetResource("invalid");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public void GetResource_Returns404_WithEmptyId()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetDefinition(""))
            .Returns(Result<ResourceDefinition>.Failure("Resource ID cannot be empty"));

        // Act
        var result = _controller.GetResource("");

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region GetResourcesByCategory Tests

    [Fact]
    public void GetResourcesByCategory_ReturnsFilteredResources()
    {
        // Arrange
        var resources = new List<ResourceDefinition>
        {
            new ResourceDefinition
            {
                ResourceId = "health",
                DisplayName = "Health",
                ShortName = "HP",
                Category = ResourceCategory.VITAL,
                DefaultMax = 100
            }
        };
        _mockResourceManager.Setup(m => m.GetDefinitionsByCategory(ResourceCategory.VITAL))
            .Returns(resources);

        // Act
        var result = _controller.GetResourcesByCategory("VITAL");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Equal("VITAL", summaries[0].Category);
    }

    [Fact]
    public void GetResourcesByCategory_Returns400_WithInvalidCategory()
    {
        // Act
        var result = _controller.GetResourcesByCategory("INVALID_CATEGORY");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetResourcesByCategory_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetDefinitionsByCategory(ResourceCategory.TACTICAL))
            .Returns(new List<ResourceDefinition>());

        // Act
        var result = _controller.GetResourcesByCategory("TACTICAL");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    #endregion

    #region GetResourcesByTag Tests

    [Fact]
    public void GetResourcesByTag_ReturnsFilteredResources()
    {
        // Arrange
        var resources = new List<ResourceDefinition>
        {
            new ResourceDefinition
            {
                ResourceId = "mana",
                DisplayName = "Mana",
                ShortName = "MP",
                Category = ResourceCategory.TACTICAL,
                DefaultMax = 100,
                Tags = new List<string> { "magic", "tactical" }
            }
        };
        _mockResourceManager.Setup(m => m.GetDefinitionsByTag("magic"))
            .Returns(resources);

        // Act
        var result = _controller.GetResourcesByTag("magic");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Contains("magic", summaries[0].Tags);
    }

    [Fact]
    public void GetResourcesByTag_Returns400_WithEmptyTag()
    {
        // Act
        var result = _controller.GetResourcesByTag("");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetResourcesByTag_Returns400_WithNullTag()
    {
        // Act
        var result = _controller.GetResourcesByTag(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetResourcesByTag_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        _mockResourceManager.Setup(m => m.GetDefinitionsByTag("nonexistent"))
            .Returns(new List<ResourceDefinition>());

        // Act
        var result = _controller.GetResourcesByTag("nonexistent");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ResourceSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    #endregion

    #region CreatePool Tests

    [Fact]
    public void CreatePool_ReturnsOk_WithValidRequest()
    {
        // Arrange
        var request = new CreatePoolRequest
        {
            ResourceId = "health",
            InitialCurrent = 75
        };
        var definition = new ResourceDefinition { ResourceId = "health", DisplayName = "Health", ShortName = "HP", Category = ResourceCategory.VITAL, DefaultMax = 100, DefaultMin = 0, DefaultCurrent = 75 };
        var pool = new ResourcePool { ResourceId = "health", Current = 75, Maximum = 100, Minimum = 0, Definition = definition };
        _mockResourceManager.Setup(m => m.CreatePool("health", 75f))
            .Returns(pool);

        // Act
        var result = _controller.CreatePool(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ResourcePoolDto>(okResult.Value);
        Assert.Equal("health", dto.ResourceId);
        Assert.Equal(75, dto.Current);
    }

    [Fact]
    public void CreatePool_Returns404_WithNonexistentResourceId()
    {
        // Arrange
        var request = new CreatePoolRequest
        {
            ResourceId = "invalid",
            InitialCurrent = 50
        };
        _mockResourceManager.Setup(m => m.CreatePool("invalid", 50f))
            .Throws(new InvalidOperationException("Resource not found: invalid"));

        // Act
        var result = _controller.CreatePool(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public void CreatePool_Returns400_WithNullRequest()
    {
        // Act
        var result = _controller.CreatePool(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void CreatePool_Returns400_WithEmptyResourceId()
    {
        // Arrange
        var request = new CreatePoolRequest
        {
            ResourceId = "",
            InitialCurrent = 50
        };

        // Act
        var result = _controller.CreatePool(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    #endregion

    #region ValidateCost Tests

    [Fact]
    public void ValidateCost_ReturnsCanAfford_WithSufficientResources()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 30,
            CurrentAmount = 100
        };
        var definition = new ResourceDefinition { ResourceId = "health", DisplayName = "Health", ShortName = "HP", Category = ResourceCategory.VITAL, DefaultMax = 100, DefaultMin = 0, DefaultCurrent = 100 };
        var pool = new ResourcePool { ResourceId = "health", Current = 100, Maximum = 100, Minimum = 0, Definition = definition };
        _mockResourceManager.Setup(m => m.ValidateCost(It.IsAny<ResourcePool>(), 30))
            .Returns(Result.Success());

        // Act
        var result = _controller.ValidateCost(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateCostResponse>(okResult.Value);
        Assert.True(response.CanAfford);
    }

    [Fact]
    public void ValidateCost_ReturnsCannotAfford_WithInsufficientResources()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 150,
            CurrentAmount = 100
        };
        var definition = new ResourceDefinition { ResourceId = "health", DisplayName = "Health", ShortName = "HP", Category = ResourceCategory.VITAL, DefaultMax = 100, DefaultMin = 0, DefaultCurrent = 100 };
        var pool = new ResourcePool { ResourceId = "health", Current = 100, Maximum = 100, Minimum = 0, Definition = definition };
        _mockResourceManager.Setup(m => m.ValidateCost(It.IsAny<ResourcePool>(), 150))
            .Returns(Result.Failure("Insufficient resources"));

        // Act
        var result = _controller.ValidateCost(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateCostResponse>(okResult.Value);
        Assert.False(response.CanAfford);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void ValidateCost_Returns400_WithNullRequest()
    {
        // Act
        var result = _controller.ValidateCost(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void ValidateCost_Returns400_WithEmptyResourceId()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "",
            Cost = 30,
            CurrentAmount = 100
        };

        // Act
        var result = _controller.ValidateCost(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void ValidateCost_Returns400_WithNegativeCost()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = -10,
            CurrentAmount = 100
        };

        // Act
        var result = _controller.ValidateCost(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void ValidateCost_Returns400_WithNegativeCurrentAmount()
    {
        // Arrange
        var request = new ValidateCostRequest
        {
            ResourceId = "health",
            Cost = 30,
            CurrentAmount = -50
        };

        // Act
        var result = _controller.ValidateCost(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    #endregion

    #region ValidateResourceDefinition Tests

    [Fact]
    public void ValidateResourceDefinition_ReturnsValid_WithValidDefinition()
    {
        // Arrange
        var request = new ResourceValidationRequest
        {
            Definition = new ResourceDefinitionDto
            {
                ResourceId = "test",
                DisplayName = "Test Resource",
                ShortName = "TR",
                Category = "VITAL",
                DefaultMax = 100,
                DefaultMin = 0,
                DefaultCurrent = 100
            }
        };
        _mockResourceManager.Setup(m => m.ValidateResourceDefinition(It.IsAny<ResourceDefinition>()))
            .Returns(Result.Success());

        // Act
        var result = _controller.ValidateResource(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ResourceValidationResponse>(okResult.Value);
        Assert.True(response.IsValid);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void ValidateResourceDefinition_ReturnsInvalid_WithMissingResourceId()
    {
        // Arrange
        var request = new ResourceValidationRequest
        {
            Definition = new ResourceDefinitionDto
            {
                ResourceId = "",
                DisplayName = "Test",
                ShortName = "T",
                Category = "VITAL",
                DefaultMax = 100
            }
        };
        _mockResourceManager.Setup(m => m.ValidateResourceDefinition(It.IsAny<ResourceDefinition>()))
            .Returns(Result.Failure("Resource ID cannot be empty"));

        // Act
        var result = _controller.ValidateResource(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ResourceValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Resource ID cannot be empty", response.Errors);
    }

    [Fact]
    public void ValidateResourceDefinition_ReturnsInvalid_WithMissingDisplayName()
    {
        // Arrange
        var request = new ResourceValidationRequest
        {
            Definition = new ResourceDefinitionDto
            {
                ResourceId = "test",
                DisplayName = "",
                ShortName = "T",
                Category = "VITAL",
                DefaultMax = 100
            }
        };
        _mockResourceManager.Setup(m => m.ValidateResourceDefinition(It.IsAny<ResourceDefinition>()))
            .Returns(Result.Failure("Display name cannot be empty"));

        // Act
        var result = _controller.ValidateResource(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ResourceValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Display name cannot be empty", response.Errors);
    }

    [Fact]
    public void ValidateResourceDefinition_ReturnsInvalid_WithInvalidMinMax()
    {
        // Arrange
        var request = new ResourceValidationRequest
        {
            Definition = new ResourceDefinitionDto
            {
                ResourceId = "test",
                DisplayName = "Test",
                ShortName = "T",
                Category = "VITAL",
                DefaultMax = 50,
                DefaultMin = 100
            }
        };
        _mockResourceManager.Setup(m => m.ValidateResourceDefinition(It.IsAny<ResourceDefinition>()))
            .Returns(Result.Failure("max cannot be less than min"));

        // Act
        var result = _controller.ValidateResource(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ResourceValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("max cannot be less than min", response.Errors);
    }

    [Fact]
    public void ValidateResourceDefinition_Returns400_WithNullRequest()
    {
        // Act
        var result = _controller.ValidateResource(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void ValidateResourceDefinition_Returns400_WithNullDefinition()
    {
        // Arrange
        var request = new ResourceValidationRequest { Definition = null! };

        // Act
        var result = _controller.ValidateResource(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    #endregion
}
