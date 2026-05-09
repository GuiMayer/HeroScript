namespace Core.Logging;

/// <summary>
/// Thread-safe factory for creating logger instances
/// </summary>
public static class LoggerFactory
{
    private static readonly object _lock = new();
    private static Func<string, ILogger> _factory = categoryName => new ConsoleLogger(categoryName, enableDebug: false);

    /// <summary>
    /// Set a custom logger factory (useful for DI integration)
    /// Thread-safe implementation
    /// </summary>
    public static void SetFactory(Func<string, ILogger> factory)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        lock (_lock)
        {
            _factory = factory;
        }
    }

    /// <summary>
    /// Create a logger for the specified category
    /// Thread-safe implementation
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        lock (_lock)
        {
            return _factory(categoryName);
        }
    }

    /// <summary>
    /// Create a logger for the specified type
    /// Thread-safe implementation
    /// </summary>
    public static ILogger CreateLogger<T>()
    {
        lock (_lock)
        {
            return _factory(typeof(T).Name);
        }
    }

    /// <summary>
    /// Reset to default console logger factory
    /// Thread-safe implementation
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _factory = categoryName => new ConsoleLogger(categoryName, enableDebug: false);
        }
    }
}
