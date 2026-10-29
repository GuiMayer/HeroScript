using System.Diagnostics;

namespace Core.Diagnostics;

/// <summary>Request-local diagnostics. Never stored in deterministic state or facts.</summary>
public sealed class OperationalTiming : IDisposable
{
    private static readonly AsyncLocal<OperationalTiming?> Current = new();
    private readonly OperationalTiming? _parent;
    private readonly AsyncLocal<Measurement?> _active = new();
    private readonly Dictionary<string, MeasurementTotals> _details = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    public sealed record MeasurementTotals(double InclusiveMs, double ExclusiveMs, long Calls);
    public static void Count(string name, long amount = 1)
    {
        if (Current.Value is not { } scope) return;
        lock (scope._durations)
            if (scope._counters.Count < 128 || scope._counters.ContainsKey(name))
                scope._counters[name] = scope._counters.GetValueOrDefault(name) + amount;
    }
    public IReadOnlyDictionary<string, MeasurementTotals> Details()
    { lock (_durations) return new Dictionary<string, MeasurementTotals>(_details); }
    public IReadOnlyDictionary<string, long> Counters()
    { lock (_durations) return new Dictionary<string, long>(_counters); }
    private readonly Dictionary<string, double> _durations = new(StringComparer.Ordinal);
    private OperationalTiming() { _parent = Current.Value; Current.Value = this; }
    public static OperationalTiming Begin() => new();
    public static IDisposable Measure(string operation) => Current.Value is { } scope
        ? new Measurement(scope, operation) : NoMeasurement.Instance;
    public IReadOnlyDictionary<string, double> Snapshot()
    { lock (_durations) return new Dictionary<string, double>(_durations); }
    public void Dispose() => Current.Value = _parent;
    private sealed class Measurement : IDisposable
    {
        private readonly OperationalTiming _scope;
        private readonly string _operation;
        private readonly Measurement? _parent;
        private readonly long _started;
        private long _children;
        private int _done;
        public Measurement(OperationalTiming scope, string operation)
        {
            _scope = scope; _operation = operation; _parent = scope._active.Value;
            _started = Stopwatch.GetTimestamp(); scope._active.Value = this;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            var ticks = Stopwatch.GetTimestamp() - _started;
            var ms = ticks * 1000d / Stopwatch.Frequency;
            var exclusive = System.Math.Max(0, ticks - Interlocked.Read(ref _children)) * 1000d / Stopwatch.Frequency;
            if (_parent != null) Interlocked.Add(ref _parent._children, ticks);
            _scope._active.Value = _parent;
            lock (_scope._durations)
            {
                _scope._durations[_operation] = _scope._durations.GetValueOrDefault(_operation) + ms;
                var old = _scope._details.GetValueOrDefault(_operation, new(0, 0, 0));
                _scope._details[_operation] = new(old.InclusiveMs + ms, old.ExclusiveMs + exclusive, old.Calls + 1);
            }
        }
    }
    private sealed class NoMeasurement : IDisposable
    { public static readonly NoMeasurement Instance = new(); public void Dispose() { } }
}
