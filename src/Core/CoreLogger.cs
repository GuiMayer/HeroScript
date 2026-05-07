using Core.Logging;

namespace Core;

/// <summary>
/// Global logger configuration for Core library
/// Provides a static logger that can be configured from the host application
/// </summary>
public static class CoreLogger
{
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Configure the global logger for Core library
    /// Call this from your application startup (e.g., Program.cs)
    /// </summary>
    public static void Configure(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get the current logger instance
    /// </summary>
    public static ILogger Current => _logger;

    /// <summary>
    /// Reset to null logger (useful for testing)
    /// </summary>
    public static void Reset()
    {
        _logger = NullLogger.Instance;
    }
}
