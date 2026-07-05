using Core.Validation;
using Xunit;

namespace Core.Tests.Validation;

/// <summary>
/// Comprehensive tests for ValidationResult class
/// Covers valid/invalid states, error/warning management, and edge cases
/// </summary>
[Trait("Category", "Unit")]
public class ValidationResultTests
{
    // ==================== CONSTRUCTION & STATE ====================
    
    [Fact]
    public void ValidationResult_DefaultConstructor_CreatesInvalidResult()
    {
        // Arrange & Act
        var result = new ValidationResult();
        
        // Assert
        // IsValid defaults to false (bool default value)
        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }
    
    [Fact]
    public void ValidationResult_WithIsValidFalse_CreatesInvalidResult()
    {
        // Arrange & Act
        var result = new ValidationResult { IsValid = false };
        
        // Assert
        Assert.False(result.IsValid);
    }
    
    [Fact]
    public void ValidationResult_NewInstance_HasEmptyCollections()
    {
        // Arrange & Act
        var result = new ValidationResult();
        
        // Assert
        Assert.NotNull(result.Errors);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }
    
    // ==================== ERROR MANAGEMENT ====================
    
    [Fact]
    public void AddError_AddsErrorToCollection()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("First error");
        
        // Assert
        Assert.Single(result.Errors);
        Assert.Contains("First error", result.Errors);
    }
    
    [Fact]
    public void AddError_MultipleErrors_AddsAllToCollection()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("Error 1");
        result.AddError("Error 2");
        result.AddError("Error 3");
        
        // Assert
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
        Assert.Contains("Error 3", result.Errors);
    }
    
    [Fact]
    public void AddError_PreservesOrder()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("First");
        result.AddError("Second");
        result.AddError("Third");
        
        // Assert
        Assert.Equal("First", result.Errors[0]);
        Assert.Equal("Second", result.Errors[1]);
        Assert.Equal("Third", result.Errors[2]);
    }
    
    [Fact]
    public void AddError_WithEmptyString_StillAdds()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("");
        
        // Assert
        Assert.Single(result.Errors);
        Assert.Contains("", result.Errors);
    }
    
    // ==================== WARNING MANAGEMENT ====================
    
    [Fact]
    public void AddWarning_AddsWarningToCollection()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddWarning("Warning message");
        
        // Assert
        Assert.Single(result.Warnings);
        Assert.Contains("Warning message", result.Warnings);
    }
    
    [Fact]
    public void AddWarning_MultipleWarnings_AddsAllToCollection()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddWarning("Warning 1");
        result.AddWarning("Warning 2");
        result.AddWarning("Warning 3");
        
        // Assert
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains("Warning 1", result.Warnings);
        Assert.Contains("Warning 2", result.Warnings);
        Assert.Contains("Warning 3", result.Warnings);
    }
    
    [Fact]
    public void AddWarning_PreservesOrder()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddWarning("First");
        result.AddWarning("Second");
        result.AddWarning("Third");
        
        // Assert
        Assert.Equal("First", result.Warnings[0]);
        Assert.Equal("Second", result.Warnings[1]);
        Assert.Equal("Third", result.Warnings[2]);
    }
    
    // ==================== MIXED ERRORS & WARNINGS ====================
    
    [Fact]
    public void ValidationResult_CanHaveBothErrorsAndWarnings()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("Error message");
        result.AddWarning("Warning message");
        
        // Assert
        Assert.Single(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Contains("Error message", result.Errors);
        Assert.Contains("Warning message", result.Warnings);
    }
    
    [Fact]
    public void ValidationResult_ErrorsAndWarnings_AreIndependent()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("Error 1");
        result.AddError("Error 2");
        result.AddWarning("Warning 1");
        
        // Assert
        Assert.Equal(2, result.Errors.Count);
        Assert.Single(result.Warnings);
    }
    
    // ==================== VALIDITY SEMANTICS ====================
    
    [Fact]
    public void ValidationResult_IsValidTrue_CanBeSetExplicitly()
    {
        // Arrange & Act
        var result = new ValidationResult { IsValid = true };
        result.AddWarning("This is just a warning");
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
    }
    
    [Fact]
    public void ValidationResult_IsValidFalse_EvenWithoutExplicitErrors()
    {
        // Arrange & Act
        var result = new ValidationResult { IsValid = false };
        
        // Assert - can be invalid without explicit error messages
        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public void ValidationResult_IsValid_CanBeSetToFalse_WithErrors()
    {
        // Arrange
        var result = new ValidationResult { IsValid = false };
        
        // Act
        result.AddError("Validation failed");
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }
    
    // ==================== PROPERTY INITIALIZATION ====================
    
    [Fact]
    public void ValidationResult_Errors_PropertyInitializer_Works()
    {
        // Arrange & Act
        var result = new ValidationResult
        {
            Errors = new List<string> { "Error 1", "Error 2" }
        };
        
        // Assert
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
    }
    
    [Fact]
    public void ValidationResult_Warnings_PropertyInitializer_Works()
    {
        // Arrange & Act
        var result = new ValidationResult
        {
            Warnings = new List<string> { "Warning 1", "Warning 2" }
        };
        
        // Assert
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("Warning 1", result.Warnings);
        Assert.Contains("Warning 2", result.Warnings);
    }
    
    [Fact]
    public void ValidationResult_FullInitialization_Works()
    {
        // Arrange & Act
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { "Error 1" },
            Warnings = new List<string> { "Warning 1" }
        };
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Single(result.Warnings);
    }
    
    // ==================== EDGE CASES ====================
    
    [Fact]
    public void ValidationResult_LargeNumberOfErrors_HandledCorrectly()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        for (int i = 0; i < 1000; i++)
        {
            result.AddError($"Error {i}");
        }
        
        // Assert
        Assert.Equal(1000, result.Errors.Count);
        Assert.Contains("Error 0", result.Errors);
        Assert.Contains("Error 999", result.Errors);
    }
    
    [Fact]
    public void ValidationResult_DuplicateErrors_BothAdded()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddError("Duplicate error");
        result.AddError("Duplicate error");
        
        // Assert
        Assert.Equal(2, result.Errors.Count);
    }
    
    [Fact]
    public void ValidationResult_DuplicateWarnings_BothAdded()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.AddWarning("Duplicate warning");
        result.AddWarning("Duplicate warning");
        
        // Assert
        Assert.Equal(2, result.Warnings.Count);
    }
    
    [Fact]
    public void ValidationResult_Errors_CanBeModifiedDirectly()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.Errors.Add("Direct add");
        
        // Assert
        Assert.Single(result.Errors);
        Assert.Contains("Direct add", result.Errors);
    }
    
    [Fact]
    public void ValidationResult_Warnings_CanBeModifiedDirectly()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act
        result.Warnings.Add("Direct add");
        
        // Assert
        Assert.Single(result.Warnings);
        Assert.Contains("Direct add", result.Warnings);
    }
    
    // ==================== REALISTIC USAGE SCENARIOS ====================
    
    [Fact]
    public void ValidationResult_ConfigValidation_Scenario()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act - simulating config validation
        result.AddError("Missing required field: 'apiKey'");
        result.AddError("Invalid value for 'timeout': must be positive");
        result.AddWarning("Field 'retries' not set, using default value");
        result.IsValid = false;
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Single(result.Warnings);
    }
    
    [Fact]
    public void ValidationResult_EntityValidation_Scenario()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act - simulating entity validation
        result.AddError("EntityId cannot be empty");
        result.AddError("Health must be between 0 and MaxHealth");
        result.AddWarning("Name is very long, may be truncated in UI");
        result.IsValid = false;
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Single(result.Warnings);
        Assert.Contains("EntityId cannot be empty", result.Errors);
    }
    
    [Fact]
    public void ValidationResult_SuccessfulValidation_Scenario()
    {
        // Arrange
        var result = new ValidationResult();
        
        // Act - simulating successful validation with warnings
        result.AddWarning("Using deprecated field 'oldFormat'");
        result.AddWarning("Consider updating to new API version");
        result.IsValid = true;
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Warnings.Count);
    }
    
    [Fact]
    public void ValidationResult_MultipleValidations_Accumulation()
    {
        // Arrange
        var result = new ValidationResult { IsValid = true };
        
        // Act - simulating multiple validation passes
        // First pass
        result.AddWarning("Pass 1: Minor issue");
        
        // Second pass finds error
        result.AddError("Pass 2: Critical issue");
        result.IsValid = false;
        
        // Third pass finds more
        result.AddError("Pass 3: Another error");
        result.AddWarning("Pass 3: Another warning");
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(2, result.Warnings.Count);
    }
}
