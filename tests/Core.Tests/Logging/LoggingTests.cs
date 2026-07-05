using System;
using System.IO;
using Core.Logging;
using Xunit;

namespace Core.Tests.Logging;

/// <summary>
/// Comprehensive tests for Logging system
/// Covers ConsoleLogger, NullLogger, LoggerFactory, and all logging scenarios
/// </summary>
[Trait("Category", "Unit")]
public class LoggingTests
{
    // Helper to capture console output
    private class ConsoleCapture : IDisposable
    {
        private readonly StringWriter _stringWriter;
        private readonly TextWriter _originalOutput;

        public ConsoleCapture()
        {
            _stringWriter = new StringWriter();
            _originalOutput = Console.Out;
            Console.SetOut(_stringWriter);
        }

        public string GetOutput() => _stringWriter.ToString();

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            _stringWriter.Dispose();
        }
    }

    // ==================== CONSOLE LOGGER - CONSTRUCTION ====================
    
    [Fact]
    public void ConsoleLogger_CanBeCreated_WithCategoryName()
    {
        // Arrange & Act
        var logger = new ConsoleLogger("TestCategory");
        
        // Assert
        Assert.NotNull(logger);
    }
    
    [Fact]
    public void ConsoleLogger_CanBeCreated_WithDebugEnabled()
    {
        // Arrange & Act
        var logger = new ConsoleLogger("TestCategory", enableDebug: true);
        
        // Assert
        Assert.NotNull(logger);
    }
    
    [Fact]
    public void ConsoleLogger_CanBeCreated_WithDebugDisabled()
    {
        // Arrange & Act
        var logger = new ConsoleLogger("TestCategory", enableDebug: false);
        
        // Assert
        Assert.NotNull(logger);
    }
    
    // ==================== CONSOLE LOGGER - LOG INFORMATION ====================
    
    [Fact]
    public void ConsoleLogger_LogInformation_WritesToConsole()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory");
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogInformation("Test message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[TestCategory]", output);
        Assert.Contains("Test message", output);
    }
    
    [Fact]
    public void ConsoleLogger_LogInformation_IncludesCategoryName()
    {
        // Arrange
        var logger = new ConsoleLogger("MyCategory");
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogInformation("Info message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[MyCategory]", output);
    }
    
    // ==================== CONSOLE LOGGER - LOG WARNING ====================
    
    [Fact]
    public void ConsoleLogger_LogWarning_WritesToConsoleWithWarningPrefix()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory");
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogWarning("Warning message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[TestCategory]", output);
        Assert.Contains("Warning:", output);
        Assert.Contains("Warning message", output);
    }
    
    // ==================== CONSOLE LOGGER - LOG ERROR ====================
    
    [Fact]
    public void ConsoleLogger_LogError_WritesToConsoleWithErrorPrefix()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory");
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogError("Error message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[TestCategory]", output);
        Assert.Contains("Error:", output);
        Assert.Contains("Error message", output);
    }
    
    [Fact]
    public void ConsoleLogger_LogError_WithException_WritesMessageAndException()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory");
        var exception = new InvalidOperationException("Something went wrong");
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogError("Error occurred", exception);
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[TestCategory]", output);
        Assert.Contains("Error:", output);
        Assert.Contains("Error occurred", output);
        Assert.Contains("Exception:", output);
        Assert.Contains("Something went wrong", output);
    }
    
    // ==================== CONSOLE LOGGER - LOG DEBUG ====================
    
    [Fact]
    public void ConsoleLogger_LogDebug_WhenDebugDisabled_DoesNotWrite()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory", enableDebug: false);
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogDebug("Debug message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Empty(output);
    }
    
    [Fact]
    public void ConsoleLogger_LogDebug_WhenDebugEnabled_WritesToConsole()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory", enableDebug: true);
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogDebug("Debug message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[TestCategory]", output);
        Assert.Contains("Debug message", output);
    }
    
    // ==================== NULL LOGGER ====================
    
    [Fact]
    public void NullLogger_HasStaticInstance()
    {
        // Arrange & Act
        var logger = NullLogger.Instance;
        
        // Assert
        Assert.NotNull(logger);
    }
    
    [Fact]
    public void NullLogger_Instance_IsSingleton()
    {
        // Arrange & Act
        var logger1 = NullLogger.Instance;
        var logger2 = NullLogger.Instance;
        
        // Assert
        Assert.Same(logger1, logger2);
    }
    
    [Fact]
    public void NullLogger_LogDebug_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger.Instance;
        
        // Act & Assert (no exception)
        logger.LogDebug("Debug message");
    }
    
    [Fact]
    public void NullLogger_LogInformation_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger.Instance;
        
        // Act & Assert
        logger.LogInformation("Info message");
    }
    
    [Fact]
    public void NullLogger_LogWarning_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger.Instance;
        
        // Act & Assert
        logger.LogWarning("Warning message");
    }
    
    [Fact]
    public void NullLogger_LogError_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger.Instance;
        
        // Act & Assert
        logger.LogError("Error message");
    }
    
    [Fact]
    public void NullLogger_LogError_WithException_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger.Instance;
        var exception = new Exception("Test exception");
        
        // Act & Assert
        logger.LogError("Error message", exception);
    }
    
    [Fact]
    public void NullLogger_DoesNotWriteToConsole()
    {
        // Arrange
        var logger = NullLogger.Instance;
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogDebug("Debug");
        logger.LogInformation("Info");
        logger.LogWarning("Warning");
        logger.LogError("Error");
        logger.LogError("Error", new Exception("Test"));
        
        // Assert
        var output = capture.GetOutput();
        Assert.Empty(output);
    }
    
    // ==================== LOGGER FACTORY - CREATE LOGGER ====================
    
    [Fact]
    public void LoggerFactory_CreateLogger_WithCategoryName_ReturnsLogger()
    {
        // Arrange
        LoggerFactory.Reset();
        
        // Act
        var logger = LoggerFactory.CreateLogger("TestCategory");
        
        // Assert
        Assert.NotNull(logger);
    }
    
    [Fact]
    public void LoggerFactory_CreateLogger_WithType_ReturnsLogger()
    {
        // Arrange
        LoggerFactory.Reset();
        
        // Act
        var logger = LoggerFactory.CreateLogger<LoggingTests>();
        
        // Assert
        Assert.NotNull(logger);
    }
    
    [Fact]
    public void LoggerFactory_CreateLogger_WithType_UsesTypeName()
    {
        // Arrange
        LoggerFactory.Reset();
        using var capture = new ConsoleCapture();
        
        // Act
        var logger = LoggerFactory.CreateLogger<LoggingTests>();
        logger.LogInformation("Test");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("[LoggingTests]", output);
    }
    
    [Fact]
    public void LoggerFactory_DefaultFactory_CreatesConsoleLogger()
    {
        // Arrange
        LoggerFactory.Reset();
        using var capture = new ConsoleCapture();
        
        // Act
        var logger = LoggerFactory.CreateLogger("TestCategory");
        logger.LogInformation("Test message");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("Test message", output);
        Assert.IsType<ConsoleLogger>(logger);
    }
    
    // ==================== LOGGER FACTORY - CUSTOM FACTORY ====================
    
    [Fact]
    public void LoggerFactory_SetFactory_ChangesLoggerCreation()
    {
        // Arrange
        LoggerFactory.Reset();
        LoggerFactory.SetFactory(_ => NullLogger.Instance);
        
        // Act
        var logger = LoggerFactory.CreateLogger("TestCategory");
        
        // Assert
        Assert.Same(NullLogger.Instance, logger);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    [Fact]
    public void LoggerFactory_SetFactory_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        LoggerFactory.Reset();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => LoggerFactory.SetFactory(null!));
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    [Fact]
    public void LoggerFactory_SetFactory_AffectsSubsequentCreations()
    {
        // Arrange
        LoggerFactory.Reset();
        var callCount = 0;
        LoggerFactory.SetFactory(category =>
        {
            callCount++;
            return new ConsoleLogger(category);
        });
        
        // Act
        LoggerFactory.CreateLogger("Category1");
        LoggerFactory.CreateLogger("Category2");
        
        // Assert
        Assert.Equal(2, callCount);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    [Fact]
    public void LoggerFactory_SetFactory_CanUseCustomLogger()
    {
        // Arrange
        LoggerFactory.Reset();
        var customLogger = new ConsoleLogger("Custom", enableDebug: true);
        LoggerFactory.SetFactory(_ => customLogger);
        
        // Act
        var logger1 = LoggerFactory.CreateLogger("Category1");
        var logger2 = LoggerFactory.CreateLogger("Category2");
        
        // Assert
        Assert.Same(customLogger, logger1);
        Assert.Same(customLogger, logger2);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    // ==================== LOGGER FACTORY - RESET ====================
    
    [Fact]
    public void LoggerFactory_Reset_RestoresDefaultFactory()
    {
        // Arrange
        LoggerFactory.SetFactory(_ => NullLogger.Instance);
        
        // Act
        LoggerFactory.Reset();
        var logger = LoggerFactory.CreateLogger("TestCategory");
        
        // Assert
        Assert.IsType<ConsoleLogger>(logger);
    }
    
    [Fact]
    public void LoggerFactory_Reset_CanBeCalledMultipleTimes()
    {
        // Arrange & Act
        LoggerFactory.Reset();
        LoggerFactory.Reset();
        LoggerFactory.Reset();
        
        // Assert
        var logger = LoggerFactory.CreateLogger("TestCategory");
        Assert.NotNull(logger);
    }
    
    // ==================== THREAD SAFETY ====================
    
    [Fact]
    public void LoggerFactory_CreateLogger_IsThreadSafe()
    {
        // Arrange
        LoggerFactory.Reset();
        var loggers = new ILogger[100];
        
        // Act
        System.Threading.Tasks.Parallel.For(0, 100, i =>
        {
            loggers[i] = LoggerFactory.CreateLogger($"Category{i}");
        });
        
        // Assert
        Assert.All(loggers, logger => Assert.NotNull(logger));
    }
    
    [Fact]
    public void LoggerFactory_SetFactory_IsThreadSafe()
    {
        // Arrange
        LoggerFactory.Reset();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        
        // Act - multiple threads setting factory concurrently
        System.Threading.Tasks.Parallel.For(0, 10, i =>
        {
            try
            {
                LoggerFactory.SetFactory(category => new ConsoleLogger(category));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        
        // Assert
        Assert.Empty(exceptions);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    // ==================== INTEGRATION SCENARIOS ====================
    
    [Fact]
    public void LoggerFactory_RealWorldScenario_DependencyInjectionSetup()
    {
        // Arrange - simulating DI setup
        LoggerFactory.Reset();
        var capturedCategories = new System.Collections.Generic.List<string>();
        
        LoggerFactory.SetFactory(category =>
        {
            capturedCategories.Add(category);
            return new ConsoleLogger(category, enableDebug: true);
        });
        
        // Act - simulating multiple components requesting loggers
        var logger1 = LoggerFactory.CreateLogger("ServiceA");
        var logger2 = LoggerFactory.CreateLogger("ServiceB");
        var logger3 = LoggerFactory.CreateLogger<LoggingTests>();
        
        // Assert
        Assert.NotNull(logger1);
        Assert.NotNull(logger2);
        Assert.NotNull(logger3);
        Assert.Equal(3, capturedCategories.Count);
        Assert.Contains("ServiceA", capturedCategories);
        Assert.Contains("ServiceB", capturedCategories);
        Assert.Contains("LoggingTests", capturedCategories);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    [Fact]
    public void LoggerFactory_RealWorldScenario_TestEnvironmentUsesNullLogger()
    {
        // Arrange - simulating test environment setup
        LoggerFactory.SetFactory(_ => NullLogger.Instance);
        using var capture = new ConsoleCapture();
        
        // Act
        var logger = LoggerFactory.CreateLogger("TestService");
        logger.LogInformation("This should not appear");
        logger.LogError("This should not appear either");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Empty(output);
        
        // Cleanup
        LoggerFactory.Reset();
    }
    
    [Fact]
    public void ConsoleLogger_MultipleMessages_AllAppearInOutput()
    {
        // Arrange
        var logger = new ConsoleLogger("TestCategory", enableDebug: true);
        using var capture = new ConsoleCapture();
        
        // Act
        logger.LogDebug("Debug 1");
        logger.LogInformation("Info 1");
        logger.LogWarning("Warning 1");
        logger.LogError("Error 1");
        
        // Assert
        var output = capture.GetOutput();
        Assert.Contains("Debug 1", output);
        Assert.Contains("Info 1", output);
        Assert.Contains("Warning 1", output);
        Assert.Contains("Error 1", output);
    }
}
