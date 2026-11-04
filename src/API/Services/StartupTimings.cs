using System.Diagnostics;
using System.Text.Json;

namespace API.Services;

/// <summary>Optional process-local startup timings, emitted only when explicitly enabled.</summary>
internal sealed class StartupTimings
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly bool _enabled = string.Equals(
        Environment.GetEnvironmentVariable("HEROSCRIPT_STARTUP_TIMINGS"), "1", StringComparison.Ordinal);

    public void Record(string phase, double startMilliseconds, string? setting = null)
    {
        if (!_enabled) return;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "startup_timing",
            phase,
            setting,
            elapsedMs = _watch.Elapsed.TotalMilliseconds - startMilliseconds,
            sinceStartMs = _watch.Elapsed.TotalMilliseconds
        }));
    }

    public double Now => _watch.Elapsed.TotalMilliseconds;
}
