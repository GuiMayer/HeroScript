namespace Core.Logging;

/// <summary>
/// Factory for creating logger instances
/// </summary>
public static class LoggerFactory
{
    private static Func<string, ILogger> _factory = categoryName => new ConsoleLogger(categoryName, enableDebug: false);

    /// <summary>
    /// Set a custom logger factory (useful for DI integration)
    /// </summary>
    public static void SetFactory(Func<string, ILogger> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Create a logger for the specified category
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        return _factory(categoryName);
    }

    /// <summary>
    /// Create a logger for the specified type
    /// </summary>
    public static ILogger CreateLogger<T>()
    {
        return _factory(typeof(T).Name);
    }

    /// <summary>
    /// Reset to default console logger factory
    /// </summary>
    public static void Reset()
    {
        _factory = categoryName => new ConsoleLogger(categoryName, enableDebug: false);
    }
}
