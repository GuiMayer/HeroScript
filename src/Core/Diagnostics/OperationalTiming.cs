using System.Diagnostics;

namespace Core.Diagnostics;

/// <summary>Request-local diagnostics. Never stored in deterministic state or facts.</summary>
public sealed class OperationalTiming : IDisposable
{
    private static readonly AsyncLocal<OperationalTiming?> Current = new();
    private readonly OperationalTiming? _parent;
    private readonly Dictionary<string, double> _durations = new(StringComparer.Ordinal);
    private OperationalTiming() { _parent = Current.Value; Current.Value = this; }
    public static OperationalTiming Begin() => new();
    public static IDisposable Measure(string operation) => Current.Value is { } scope
        ? new Measurement(scope, operation) : NoMeasurement.Instance;
    public IReadOnlyDictionary<string, double> Snapshot()
    { lock (_durations) return new Dictionary<string, double>(_durations); }
    public void Dispose() => Current.Value = _parent;
    private sealed class Measurement(OperationalTiming scope, string operation) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            var ms = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            lock (scope._durations) scope._durations[operation] = scope._durations.GetValueOrDefault(operation) + ms;
        }
    }
    private sealed class NoMeasurement : IDisposable
    { public static readonly NoMeasurement Instance = new(); public void Dispose() { } }
}
