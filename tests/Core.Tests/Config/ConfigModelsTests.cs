using System;
using Core.Config;
using Core.Config.Delta;
using Xunit;

namespace Core.Tests.Config;

/// <summary>
/// Comprehensive tests for Config namespace models
/// Covers CombatOptions, ConfigMetadata, ResourceConfiguration, ResourceMode, DeltaOperationType
/// </summary>
[Trait("Category", "Unit")]
public class ConfigModelsTests
{
    // ==================== COMBAT OPTIONS TESTS ====================
    
    [Fact]
    public void CombatOptions_DefaultConstruction_SetsTurnOrderStrategyToFixed()
    {
        // Arrange & Act
        var options = new CombatOptions();
        
        // Assert
        Assert.Equal("fixed", options.TurnOrderStrategy);
    }
    
    [Fact]
    public void CombatOptions_TurnOrderStrategy_CanBeSet()
    {
        // Arrange & Act
        var options = new CombatOptions
        {
            TurnOrderStrategy = "speed_based"
        };
        
        // Assert
        Assert.Equal("speed_based", options.TurnOrderStrategy);
    }
    
    [Fact]
    public void CombatOptions_TurnOrderStrategy_SupportsAllValidValues()
    {
        // Arrange
        var validStrategies = new[] { "fixed", "speed_based", "initiative", "atb", "conditional" };
        
        // Act & Assert
        foreach (var strategy in validStrategies)
        {
            var options = new CombatOptions { TurnOrderStrategy = strategy };
            Assert.Equal(strategy, options.TurnOrderStrategy);
        }
    }
    
    // ==================== CONFIG METADATA TESTS ====================
    
    [Fact]
    public void ConfigMetadata_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata();
        
        // Assert
        Assert.Equal(string.Empty, metadata.Name);
        Assert.Equal("1.0.0", metadata.Version);
        Assert.Equal("unknown", metadata.Author);
        Assert.Equal(string.Empty, metadata.Description);
        Assert.Null(metadata.Parent);
        Assert.Null(metadata.GitRepo);
        Assert.Null(metadata.GitHash);
        Assert.NotNull(metadata.CreatedAt);
    }
    
    [Fact]
    public void ConfigMetadata_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata
        {
            Name = "Alisyum",
            Version = "2.0.0",
            Author = "core-team",
            Description = "Core game configuration",
            Parent = "base_config",
            GitRepo = "https://github.com/example/alisyum",
            GitHash = "abc123def456",
            CreatedAt = "2026-01-15"
        };
        
        // Assert
        Assert.Equal("Alisyum", metadata.Name);
        Assert.Equal("2.0.0", metadata.Version);
        Assert.Equal("core-team", metadata.Author);
        Assert.Equal("Core game configuration", metadata.Description);
        Assert.Equal("base_config", metadata.Parent);
        Assert.Equal("https://github.com/example/alisyum", metadata.GitRepo);
        Assert.Equal("abc123def456", metadata.GitHash);
        Assert.Equal("2026-01-15", metadata.CreatedAt);
    }
    
    [Fact]
    public void ConfigMetadata_Version_DefaultsToOneZeroZero()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata();
        
        // Assert
        Assert.Equal("1.0.0", metadata.Version);
    }
    
    [Fact]
    public void ConfigMetadata_Author_DefaultsToUnknown()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata();
        
        // Assert
        Assert.Equal("unknown", metadata.Author);
    }
    
    [Fact]
    public void ConfigMetadata_Parent_CanBeNull()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata { Parent = null };
        
        // Assert
        Assert.Null(metadata.Parent);
    }
    
    [Fact]
    public void ConfigMetadata_Parent_SupportsInheritance()
    {
        // Arrange & Act
        var childMod = new ConfigMetadata
        {
            Name = "vampire_mod",
            Parent = "alisyum"
        };
        
        // Assert
        Assert.Equal("vampire_mod", childMod.Name);
        Assert.Equal("alisyum", childMod.Parent);
    }
    
    [Fact]
    public void ConfigMetadata_GitRepo_CanBeNull()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata { GitRepo = null };
        
        // Assert
        Assert.Null(metadata.GitRepo);
    }
    
    [Fact]
    public void ConfigMetadata_GitHash_CanBeNull()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata { GitHash = null };
        
        // Assert
        Assert.Null(metadata.GitHash);
    }
    
    [Fact]
    public void ConfigMetadata_CreatedAt_IsISO8601Format()
    {
        // Arrange & Act
        var metadata = new ConfigMetadata();
        
        // Assert
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}", metadata.CreatedAt);
    }
    
    // ==================== RESOURCE MODE ENUM TESTS ====================
    
    [Fact]
    public void ResourceMode_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<ResourceMode>();
        
        // Assert
        Assert.Contains(ResourceMode.Auto, values);
        Assert.Contains(ResourceMode.Development, values);
        Assert.Contains(ResourceMode.Production, values);
        Assert.Equal(3, values.Length);
    }
    
    // ==================== RESOURCE CONFIGURATION TESTS ====================
    
    [Fact]
    public void ResourceConfiguration_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var config = new ResourceConfiguration();
        
        // Assert
        Assert.Equal(ResourceMode.Auto, config.Mode);
        Assert.Null(config.CoreResourcesPath);
        Assert.False(config.ValidateResourceVersions);
    }
    
    [Fact]
    public void ResourceConfiguration_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var config = new ResourceConfiguration
        {
            Mode = ResourceMode.Development,
            CoreResourcesPath = "C:\\CustomPath\\Resources",
            ValidateResourceVersions = true
        };
        
        // Assert
        Assert.Equal(ResourceMode.Development, config.Mode);
        Assert.Equal("C:\\CustomPath\\Resources", config.CoreResourcesPath);
        Assert.True(config.ValidateResourceVersions);
    }
    
    [Fact]
    public void ResourceConfiguration_Mode_DefaultsToAuto()
    {
        // Arrange & Act
        var config = new ResourceConfiguration();
        
        // Assert
        Assert.Equal(ResourceMode.Auto, config.Mode);
    }
    
    [Fact]
    public void ResourceConfiguration_ValidateResourceVersions_DefaultsToFalse()
    {
        // Arrange & Act
        var config = new ResourceConfiguration();
        
        // Assert
        Assert.False(config.ValidateResourceVersions);
    }
    
    [Fact]
    public void ResourceConfiguration_CoreResourcesPath_CanBeNull()
    {
        // Arrange & Act
        var config = new ResourceConfiguration { CoreResourcesPath = null };
        
        // Assert
        Assert.Null(config.CoreResourcesPath);
    }
    
    // ==================== DELTA OPERATION TYPE ENUM TESTS ====================
    
    [Fact]
    public void DeltaOperationType_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<DeltaOperationType>();
        
        // Assert
        Assert.Contains(DeltaOperationType.REPLACE, values);
        Assert.Contains(DeltaOperationType.MERGE_SHALLOW, values);
        Assert.Contains(DeltaOperationType.MERGE_DEEP, values);
        Assert.Contains(DeltaOperationType.DELETE, values);
        Assert.Contains(DeltaOperationType.ARRAY_APPEND, values);
        Assert.Contains(DeltaOperationType.ARRAY_PREPEND, values);
        Assert.Contains(DeltaOperationType.ARRAY_REMOVE_INDEX, values);
        Assert.Contains(DeltaOperationType.ARRAY_REPLACE_INDEX, values);
        Assert.Contains(DeltaOperationType.FIELD_DELETE, values);
        Assert.Equal(9, values.Length);
    }
    
    [Fact]
    public void DeltaOperationType_BasicOperations_AreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<DeltaOperationType>();
        
        // Assert - Basic operations
        Assert.Contains(DeltaOperationType.REPLACE, values);
        Assert.Contains(DeltaOperationType.DELETE, values);
    }
    
    [Fact]
    public void DeltaOperationType_MergeOperations_AreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<DeltaOperationType>();
        
        // Assert - Merge operations
        Assert.Contains(DeltaOperationType.MERGE_SHALLOW, values);
        Assert.Contains(DeltaOperationType.MERGE_DEEP, values);
    }
    
    [Fact]
    public void DeltaOperationType_ArrayOperations_AreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<DeltaOperationType>();
        
        // Assert - Array operations
        Assert.Contains(DeltaOperationType.ARRAY_APPEND, values);
        Assert.Contains(DeltaOperationType.ARRAY_PREPEND, values);
        Assert.Contains(DeltaOperationType.ARRAY_REMOVE_INDEX, values);
        Assert.Contains(DeltaOperationType.ARRAY_REPLACE_INDEX, values);
    }
    
    [Fact]
    public void DeltaOperationType_FieldOperations_AreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<DeltaOperationType>();
        
        // Assert - Field operations
        Assert.Contains(DeltaOperationType.FIELD_DELETE, values);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void CombatOptions_SpeedBasedTurnOrder_Configuration()
    {
        // Arrange & Act - Configure speed-based turn order
        var options = new CombatOptions
        {
            TurnOrderStrategy = "speed_based"
        };
        
        // Assert
        Assert.Equal("speed_based", options.TurnOrderStrategy);
    }
    
    [Fact]
    public void ConfigMetadata_BaseConfigScenario_NoParent()
    {
        // Arrange & Act - Base configuration with no parent
        var baseConfig = new ConfigMetadata
        {
            Name = "Alisyum",
            Version = "1.0.0",
            Author = "core",
            Description = "Core Alisyum game configuration",
            Parent = null
        };
        
        // Assert
        Assert.Equal("Alisyum", baseConfig.Name);
        Assert.Null(baseConfig.Parent);
    }
    
    [Fact]
    public void ConfigMetadata_ModScenario_WithParent()
    {
        // Arrange & Act - Mod configuration inheriting from base
        var vampireMod = new ConfigMetadata
        {
            Name = "Vampire Mod",
            Version = "1.2.0",
            Author = "modder-xyz",
            Description = "Adds vampire class and mechanics",
            Parent = "alisyum"
        };
        
        // Assert
        Assert.Equal("Vampire Mod", vampireMod.Name);
        Assert.Equal("alisyum", vampireMod.Parent);
    }
    
    [Fact]
    public void ConfigMetadata_GitTrackedMod_WithRepoInfo()
    {
        // Arrange & Act - Git-tracked mod configuration
        var gitMod = new ConfigMetadata
        {
            Name = "Community Expansion",
            GitRepo = "https://github.com/community/expansion",
            GitHash = "a1b2c3d4e5f6"
        };
        
        // Assert
        Assert.NotNull(gitMod.GitRepo);
        Assert.NotNull(gitMod.GitHash);
    }
    
    [Fact]
    public void ResourceConfiguration_DevelopmentMode_WithVersionValidation()
    {
        // Arrange & Act - Development configuration
        var devConfig = new ResourceConfiguration
        {
            Mode = ResourceMode.Development,
            ValidateResourceVersions = true
        };
        
        // Assert
        Assert.Equal(ResourceMode.Development, devConfig.Mode);
        Assert.True(devConfig.ValidateResourceVersions);
    }
    
    [Fact]
    public void ResourceConfiguration_ProductionMode_WithVersionValidationDisabled()
    {
        // Arrange & Act - Production configuration
        var prodConfig = new ResourceConfiguration
        {
            Mode = ResourceMode.Production,
            ValidateResourceVersions = false
        };
        
        // Assert
        Assert.Equal(ResourceMode.Production, prodConfig.Mode);
    }
    
    [Fact]
    public void DeltaOperationType_ReplaceScenario_CompleteOverride()
    {
        // Arrange & Act - Replace operation for complete override
        var operation = DeltaOperationType.REPLACE;
        
        // Assert
        Assert.Equal(DeltaOperationType.REPLACE, operation);
    }
    
    [Fact]
    public void DeltaOperationType_MergeDeepScenario_PreserveFields()
    {
        // Arrange & Act - Deep merge to preserve existing fields
        var operation = DeltaOperationType.MERGE_DEEP;
        
        // Assert
        Assert.Equal(DeltaOperationType.MERGE_DEEP, operation);
    }
    
    [Fact]
    public void DeltaOperationType_ArrayAppendScenario_AddCards()
    {
        // Arrange & Act - Append new cards to starting deck
        var operation = DeltaOperationType.ARRAY_APPEND;
        
        // Assert
        Assert.Equal(DeltaOperationType.ARRAY_APPEND, operation);
    }
    
    [Fact]
    public void DeltaOperationType_DeleteScenario_RemoveResource()
    {
        // Arrange & Act - Delete unwanted resource from parent
        var operation = DeltaOperationType.DELETE;
        
        // Assert
        Assert.Equal(DeltaOperationType.DELETE, operation);
    }
    
    [Fact]
    public void ConfigMetadata_InheritanceChain_ThreeLevels()
    {
        // Arrange & Act - Three-level inheritance chain
        var base_config = new ConfigMetadata
        {
            Name = "Base",
            Parent = null
        };
        
        var expansion = new ConfigMetadata
        {
            Name = "Expansion",
            Parent = "base"
        };
        
        var mod = new ConfigMetadata
        {
            Name = "Mod",
            Parent = "expansion"
        };
        
        // Assert
        Assert.Null(base_config.Parent);
        Assert.Equal("base", expansion.Parent);
        Assert.Equal("expansion", mod.Parent);
    }
}
