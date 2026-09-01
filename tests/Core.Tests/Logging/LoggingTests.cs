using Core.Logging;
using Xunit;

namespace Core.Tests.Logging;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleLoggingTestCollection
{
    public const string Name = "Console logging";
}

/// <summary>
/// Console is process-global, so capture tests run outside xUnit's parallel
/// collections. Runtime services use injected loggers and never mutate a
/// process-global logger factory.
/// </summary>
[Trait("Category", "Unit")]
[Collection(ConsoleLoggingTestCollection.Name)]
public sealed class LoggingTests
{
    [Fact]
    public void ConsoleLogger_InformationIncludesCategoryAndMessage()
    {
        using var capture = new ConsoleCapture();

        new ConsoleLogger("TestCategory").LogInformation("Test message");

        Assert.Contains("[TestCategory]", capture.Output);
        Assert.Contains("Test message", capture.Output);
    }

    [Fact]
    public void ConsoleLogger_WarningIncludesSeverity()
    {
        using var capture = new ConsoleCapture();

        new ConsoleLogger("TestCategory").LogWarning("Warning message");

        Assert.Contains("Warning:", capture.Output);
        Assert.Contains("Warning message", capture.Output);
    }

    [Fact]
    public void ConsoleLogger_ErrorIncludesException()
    {
        using var capture = new ConsoleCapture();

        new ConsoleLogger("TestCategory").LogError(
            "Error occurred",
            new InvalidOperationException("Something went wrong"));

        Assert.Contains("Error occurred", capture.Output);
        Assert.Contains("Exception:", capture.Output);
        Assert.Contains("Something went wrong", capture.Output);
    }

    [Fact]
    public void ConsoleLogger_DebugHonorsConfiguration()
    {
        using var capture = new ConsoleCapture();
        var disabled = new ConsoleLogger("Disabled", enableDebug: false);
        var enabled = new ConsoleLogger("Enabled", enableDebug: true);

        disabled.LogDebug("hidden");
        enabled.LogDebug("visible");

        Assert.DoesNotContain("hidden", capture.Output);
        Assert.Contains("visible", capture.Output);
    }

    [Fact]
    public void NullLogger_IsSingletonAndProducesNoOutput()
    {
        using var capture = new ConsoleCapture();
        var logger = NullLogger.Instance;

        logger.LogDebug("Debug");
        logger.LogInformation("Info");
        logger.LogWarning("Warning");
        logger.LogError("Error");
        logger.LogError("Error", new Exception("Test"));

        Assert.Same(logger, NullLogger.Instance);
        Assert.Empty(capture.Output);
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly StringWriter _writer = new();
        private readonly TextWriter _originalOutput = Console.Out;

        public ConsoleCapture() => Console.SetOut(_writer);

        public string Output => _writer.ToString();

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            _writer.Dispose();
        }
    }
}
