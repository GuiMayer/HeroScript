using System.Text.Json;

namespace API.Services;

/// <summary>Publishes optional ephemeral startup progress outside authoritative state.</summary>
internal sealed class StartupProgress
{
    private readonly string? _path = Environment.GetEnvironmentVariable("HEROSCRIPT_STARTUP_PROGRESS_PATH");
    private readonly string? _attempt = Environment.GetEnvironmentVariable("HEROSCRIPT_STARTUP_ATTEMPT");

    public void Report(string phase, string? setting = null)
    {
        if (string.IsNullOrWhiteSpace(_path) || string.IsNullOrWhiteSpace(_attempt)) return;
        var temporary = _path + "." + Environment.ProcessId + ".tmp";
        try
        {
            var message = JsonSerializer.Serialize(new
            {
                version = 1,
                attemptId = _attempt,
                phase,
                setting,
                timestampUtc = DateTimeOffset.UtcNow
            });
            File.WriteAllText(temporary, message);
            File.Move(temporary, _path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
