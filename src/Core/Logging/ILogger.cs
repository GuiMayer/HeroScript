namespace Core.Logging;

/// <summary>
/// Simple logging abstraction for Core library
/// </summary>
public interface ILogger
{
    void LogDebug(string message);
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogError(string message, Exception exception);
}
