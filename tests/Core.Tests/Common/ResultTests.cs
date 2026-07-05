using System;
using System.Collections.Generic;
using System.Linq;
using Core.Common;
using Xunit;

namespace Core.Tests.Common;

/// <summary>
/// Comprehensive tests for Result and Result&lt;T&gt; classes
/// Covers success/failure paths, functional operations, and edge cases
/// </summary>
[Trait("Category", "Unit")]
public class ResultTests
{
    // ==================== RESULT<T> - SUCCESS TESTS ====================
    
    [Fact]
    public void Result_Success_CreatesSuccessfulResult()
    {
        // Arrange & Act
        var result = Result<int>.Success(42);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }
    
    [Fact]
    public void Result_Success_WithReferenceType_StoresValue()
    {
        // Arrange & Act
        var result = Result<string>.Success("test");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value);
    }
    
    [Fact]
    public void Result_Success_ExceptionIsNull()
    {
        // Arrange & Act
        var result = Result<int>.Success(10);
        
        // Assert
        Assert.Null(result.Exception);
    }
    
    [Fact]
    public void Result_Success_AccessingError_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.Contains("Cannot access Error of a successful result", ex.Message);
    }
    
    [Fact]
    public void Result_Success_ToString_ReturnsFormattedString()
    {
        // Arrange & Act
        var result = Result<int>.Success(42);
        
        // Assert
        Assert.Equal("Success(42)", result.ToString());
    }
    
    // ==================== RESULT<T> - FAILURE TESTS ====================
    
    [Fact]
    public void Result_Failure_CreatesFailedResult()
    {
        // Arrange & Act
        var result = Result<int>.Failure("Something went wrong");
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }
    
    [Fact]
    public void Result_Failure_WithException_StoresException()
    {
        // Arrange
        var exception = new ArgumentException("Bad argument");
        
        // Act
        var result = Result<int>.Failure("Error occurred", exception);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error occurred", result.Error);
        Assert.Same(exception, result.Exception);
    }
    
    [Fact]
    public void Result_Failure_AccessingValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result<int>.Failure("Error");
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Cannot access Value of a failed result", ex.Message);
        Assert.Contains("Error", ex.Message);
    }
    
    [Fact]
    public void Result_Failure_ToString_ReturnsFormattedString()
    {
        // Arrange & Act
        var result = Result<int>.Failure("Operation failed");
        
        // Assert
        Assert.Equal("Failure(Operation failed)", result.ToString());
    }
    
    // ==================== RESULT<T> - IMPLICIT CONVERSION ====================
    
    [Fact]
    public void Result_ImplicitConversion_FromValue_CreatesSuccess()
    {
        // Arrange & Act
        Result<int> result = 42;
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }
    
    [Fact]
    public void Result_ImplicitConversion_FromString_CreatesSuccess()
    {
        // Arrange & Act
        Result<string> result = "test value";
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test value", result.Value);
    }
    
    // ==================== RESULT<T> - FUNCTIONAL OPERATIONS ====================
    
    [Fact]
    public void Result_OnSuccess_ExecutesActionWhenSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);
        int capturedValue = 0;
        
        // Act
        var returned = result.OnSuccess(v => capturedValue = v);
        
        // Assert
        Assert.Equal(42, capturedValue);
        Assert.Same(result, returned); // Should return same instance for chaining
    }
    
    [Fact]
    public void Result_OnSuccess_DoesNotExecuteWhenFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Error");
        bool executed = false;
        
        // Act
        result.OnSuccess(_ => executed = true);
        
        // Assert
        Assert.False(executed);
    }
    
    [Fact]
    public void Result_OnFailure_ExecutesActionWhenFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Something failed");
        string capturedError = string.Empty;
        
        // Act
        var returned = result.OnFailure(e => capturedError = e);
        
        // Assert
        Assert.Equal("Something failed", capturedError);
        Assert.Same(result, returned);
    }
    
    [Fact]
    public void Result_OnFailure_DoesNotExecuteWhenSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);
        bool executed = false;
        
        // Act
        result.OnFailure(_ => executed = true);
        
        // Assert
        Assert.False(executed);
    }
    
    [Fact]
    public void Result_Map_TransformsSuccessValue()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var mapped = result.Map(x => x * 2);
        
        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(20, mapped.Value);
    }
    
    [Fact]
    public void Result_Map_PropagatesFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Original error");
        
        // Act
        var mapped = result.Map(x => x * 2);
        
        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("Original error", mapped.Error);
    }
    
    [Fact]
    public void Result_Map_CatchesExceptionInMapper()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var mapped = result.Map<int>(_ => throw new InvalidOperationException("Mapper failed"));
        
        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Contains("Mapping failed", mapped.Error);
        Assert.NotNull(mapped.Exception);
        Assert.IsType<InvalidOperationException>(mapped.Exception);
    }
    
    [Fact]
    public void Result_Bind_ChainsSuccessfulOperation()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var bound = result.Bind(x => Result<string>.Success($"Value: {x}"));
        
        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal("Value: 10", bound.Value);
    }
    
    [Fact]
    public void Result_Bind_PropagatesFailureFromOriginal()
    {
        // Arrange
        var result = Result<int>.Failure("Original error");
        
        // Act
        var bound = result.Bind(x => Result<string>.Success($"Value: {x}"));
        
        // Assert
        Assert.True(bound.IsFailure);
        Assert.Equal("Original error", bound.Error);
    }
    
    [Fact]
    public void Result_Bind_PropagatesFailureFromBinder()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var bound = result.Bind<string>(_ => Result<string>.Failure("Binder failed"));
        
        // Assert
        Assert.True(bound.IsFailure);
        Assert.Equal("Binder failed", bound.Error);
    }
    
    [Fact]
    public void Result_Bind_CatchesExceptionInBinder()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var bound = result.Bind<string>(_ => throw new InvalidOperationException("Binder crashed"));
        
        // Assert
        Assert.True(bound.IsFailure);
        Assert.Contains("Binding failed", bound.Error);
        Assert.NotNull(bound.Exception);
    }
    
    [Fact]
    public void Result_ValueOr_ReturnsValueWhenSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);
        
        // Act
        var value = result.ValueOr(0);
        
        // Assert
        Assert.Equal(42, value);
    }
    
    [Fact]
    public void Result_ValueOr_ReturnsDefaultWhenFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Error");
        
        // Act
        var value = result.ValueOr(999);
        
        // Assert
        Assert.Equal(999, value);
    }
    
    [Fact]
    public void Result_ValueOrFunc_ReturnsValueWhenSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);
        
        // Act
        var value = result.ValueOr(() => 0);
        
        // Assert
        Assert.Equal(42, value);
    }
    
    [Fact]
    public void Result_ValueOrFunc_ComputesDefaultWhenFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Error");
        bool providerCalled = false;
        
        // Act
        var value = result.ValueOr(() =>
        {
            providerCalled = true;
            return 999;
        });
        
        // Assert
        Assert.Equal(999, value);
        Assert.True(providerCalled);
    }
    
    [Fact]
    public void Result_Match_CallsOnSuccessWhenSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);
        
        // Act
        var output = result.Match(
            onSuccess: v => $"Success: {v}",
            onFailure: e => $"Failure: {e}"
        );
        
        // Assert
        Assert.Equal("Success: 42", output);
    }
    
    [Fact]
    public void Result_Match_CallsOnFailureWhenFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Something broke");
        
        // Act
        var output = result.Match(
            onSuccess: v => $"Success: {v}",
            onFailure: e => $"Failure: {e}"
        );
        
        // Assert
        Assert.Equal("Failure: Something broke", output);
    }
    
    // ==================== RESULT (NON-GENERIC) TESTS ====================
    
    [Fact]
    public void Result_Success_CreatesSuccessfulVoidResult()
    {
        // Arrange & Act
        var result = Result.Success();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }
    
    [Fact]
    public void Result_Failure_CreatesFailedVoidResult()
    {
        // Arrange & Act
        var result = Result.Failure("Operation failed");
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Operation failed", result.Error);
    }
    
    [Fact]
    public void Result_VoidSuccess_AccessingError_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }
    
    [Fact]
    public void Result_VoidSuccess_ToString_ReturnsSuccess()
    {
        // Arrange & Act
        var result = Result.Success();
        
        // Assert
        Assert.Equal("Success", result.ToString());
    }
    
    [Fact]
    public void Result_VoidFailure_ToString_ReturnsFormattedString()
    {
        // Arrange & Act
        var result = Result.Failure("Error occurred");
        
        // Assert
        Assert.Equal("Failure(Error occurred)", result.ToString());
    }
    
    [Fact]
    public void Result_Void_OnSuccess_ExecutesWhenSuccess()
    {
        // Arrange
        var result = Result.Success();
        bool executed = false;
        
        // Act
        result.OnSuccess(() => executed = true);
        
        // Assert
        Assert.True(executed);
    }
    
    [Fact]
    public void Result_Void_OnFailure_ExecutesWhenFailure()
    {
        // Arrange
        var result = Result.Failure("Error");
        string capturedError = string.Empty;
        
        // Act
        result.OnFailure(e => capturedError = e);
        
        // Assert
        Assert.Equal("Error", capturedError);
    }
    
    [Fact]
    public void Result_Void_Map_TransformsToTypedResult()
    {
        // Arrange
        var result = Result.Success();
        
        // Act
        var mapped = result.Map(() => 42);
        
        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
    }
    
    [Fact]
    public void Result_Void_Map_PropagatesFailure()
    {
        // Arrange
        var result = Result.Failure("Original error");
        
        // Act
        var mapped = result.Map(() => 42);
        
        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("Original error", mapped.Error);
    }
    
    [Fact]
    public void Result_Void_Match_CallsCorrectBranch()
    {
        // Arrange
        var success = Result.Success();
        var failure = Result.Failure("Error");
        
        // Act
        var successOutput = success.Match(() => "OK", e => $"Fail: {e}");
        var failureOutput = failure.Match(() => "OK", e => $"Fail: {e}");
        
        // Assert
        Assert.Equal("OK", successOutput);
        Assert.Equal("Fail: Error", failureOutput);
    }
    
    // ==================== RESULT EXTENSIONS - COMBINE ====================
    
    [Fact]
    public void ResultExtensions_Combine_AllSuccess_ReturnsSuccessWithValues()
    {
        // Arrange
        var results = new[]
        {
            Result<int>.Success(1),
            Result<int>.Success(2),
            Result<int>.Success(3)
        };
        
        // Act
        var combined = results.Combine();
        
        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Equal(3, combined.Value.Count);
        Assert.Equal(new[] { 1, 2, 3 }, combined.Value);
    }
    
    [Fact]
    public void ResultExtensions_Combine_AnyFailure_ReturnsFailureWithAllErrors()
    {
        // Arrange
        var results = new[]
        {
            Result<int>.Success(1),
            Result<int>.Failure("Error 1"),
            Result<int>.Success(3),
            Result<int>.Failure("Error 2")
        };
        
        // Act
        var combined = results.Combine();
        
        // Assert
        Assert.True(combined.IsFailure);
        Assert.Contains("Error 1", combined.Error);
        Assert.Contains("Error 2", combined.Error);
        Assert.Contains("Multiple failures", combined.Error);
    }
    
    [Fact]
    public void ResultExtensions_Combine_EmptyList_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        var results = Enumerable.Empty<Result<int>>();
        
        // Act
        var combined = results.Combine();
        
        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Empty(combined.Value);
    }
    
    // ==================== RESULT EXTENSIONS - TRY ====================
    
    [Fact]
    public void ResultExtensions_Try_SuccessfulAction_ReturnsSuccess()
    {
        // Arrange & Act
        var result = ResultExtensions.Try(() => 42);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }
    
    [Fact]
    public void ResultExtensions_Try_ThrowingAction_ReturnsFailure()
    {
        // Arrange & Act
        var result = ResultExtensions.Try<int>(() => throw new InvalidOperationException("Test exception"));
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Operation failed", result.Error);
        Assert.Contains("Test exception", result.Error);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }
    
    [Fact]
    public void ResultExtensions_Try_WithCustomErrorMessage_UsesCustomMessage()
    {
        // Arrange & Act
        var result = ResultExtensions.Try<int>(
            () => throw new InvalidOperationException("Inner exception"),
            "Custom error message"
        );
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Custom error message", result.Error);
        Assert.NotNull(result.Exception);
    }
    
    [Fact]
    public void ResultExtensions_TryVoid_SuccessfulAction_ReturnsSuccess()
    {
        // Arrange
        bool executed = false;
        
        // Act
        var result = ResultExtensions.Try(() => executed = true);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(executed);
    }
    
    [Fact]
    public void ResultExtensions_TryVoid_ThrowingAction_ReturnsFailure()
    {
        // Arrange & Act
        var result = ResultExtensions.Try(() => throw new InvalidOperationException("Action failed"));
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Operation failed", result.Error);
        Assert.NotNull(result.Exception);
    }
    
    // ==================== EDGE CASES ====================
    
    [Fact]
    public void Result_Success_WithNull_AllowsNullValue()
    {
        // Arrange & Act
        var result = Result<string?>.Success(null);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
    
    [Fact]
    public void Result_Chaining_MultipleOperations_WorksCorrectly()
    {
        // Arrange
        var result = Result<int>.Success(10);
        
        // Act
        var final = result
            .Map(x => x * 2)
            .Bind(x => Result<string>.Success($"Value: {x}"))
            .Map(s => s.ToUpper())
            .OnSuccess(s => Console.WriteLine(s));
        
        // Assert
        Assert.True(final.IsSuccess);
        Assert.Equal("VALUE: 20", final.Value);
    }
    
    [Fact]
    public void Result_Chaining_BreaksOnFirstFailure()
    {
        // Arrange
        var result = Result<int>.Success(10);
        bool thirdOperationCalled = false;
        
        // Act
        var final = result
            .Map(x => x * 2)
            .Bind<string>(_ => Result<string>.Failure("Second operation failed"))
            .Map(s =>
            {
                thirdOperationCalled = true;
                return s.ToUpper();
            });
        
        // Assert
        Assert.True(final.IsFailure);
        Assert.Equal("Second operation failed", final.Error);
        Assert.False(thirdOperationCalled); // Should not execute after failure
    }
    
    [Fact]
    public void Result_ExceptionPropagation_MaintainsOriginalException()
    {
        // Arrange
        var originalException = new ArgumentException("Original");
        var result = Result<int>.Failure("Error", originalException);
        
        // Act
        var mapped = result.Map(x => x * 2);
        var bound = mapped.Bind(x => Result<string>.Success("test"));
        
        // Assert
        Assert.Same(originalException, mapped.Exception);
        Assert.Same(originalException, bound.Exception);
    }
}
