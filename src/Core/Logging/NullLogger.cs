namespace Core.Logging;

/// <summary>
/// Null logger implementation that discards all log messages
/// Useful for testing and scenarios where logging is not needed
/// </summary>
public class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    private NullLogger() { }

    public void LogDebug(string message) { }
    public void LogInformation(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message) { }
    public void LogError(string message, Exception exception) { }
}
