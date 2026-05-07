namespace Core.Logging;

/// <summary>
/// Console-based logger implementation for Core library
/// </summary>
public class ConsoleLogger : ILogger
{
    private readonly string _categoryName;
    private readonly bool _enableDebug;

    public ConsoleLogger(string categoryName, bool enableDebug = false)
    {
        _categoryName = categoryName;
        _enableDebug = enableDebug;
    }

    public void LogDebug(string message)
    {
        if (_enableDebug)
        {
            Console.WriteLine($"[{_categoryName}] {message}");
        }
    }

    public void LogInformation(string message)
    {
        Console.WriteLine($"[{_categoryName}] {message}");
    }

    public void LogWarning(string message)
    {
        Console.WriteLine($"[{_categoryName}] Warning: {message}");
    }

    public void LogError(string message)
    {
        Console.WriteLine($"[{_categoryName}] Error: {message}");
    }

    public void LogError(string message, Exception exception)
    {
        Console.WriteLine($"[{_categoryName}] Error: {message}");
        Console.WriteLine($"[{_categoryName}] Exception: {exception.Message}");
    }
}
