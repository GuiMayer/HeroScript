using Microsoft.Extensions.Logging;
using CoreLogging = Core.Logging;

namespace API.Logging;

/// <summary>
/// Adapter that bridges Microsoft.Extensions.Logging to Core.Logging.ILogger
/// </summary>
public class CoreLoggerAdapter : CoreLogging.ILogger
{
    private readonly ILogger _msLogger;

    public CoreLoggerAdapter(ILogger msLogger)
    {
        _msLogger = msLogger ?? throw new ArgumentNullException(nameof(msLogger));
    }

    public void LogDebug(string message)
    {
        _msLogger.LogDebug(message);
    }

    public void LogInformation(string message)
    {
        _msLogger.LogInformation(message);
    }

    public void LogWarning(string message)
    {
        _msLogger.LogWarning(message);
    }

    public void LogError(string message)
    {
        _msLogger.LogError(message);
    }

    public void LogError(string message, Exception exception)
    {
        _msLogger.LogError(exception, message);
    }
}
