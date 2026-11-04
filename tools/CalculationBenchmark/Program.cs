using System.Diagnostics;
using System.Text.Json;
using Core.Calculations;

if (args.Length != 1) throw new ArgumentException("Usage: CalculationBenchmark <cases.json>");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var cases = JsonSerializer.Deserialize<BenchmarkCase[]>(File.ReadAllText(args[0]), options) ?? throw new InvalidDataException("Missing cases");
var engine = new CalculationEngine();
foreach (var item in cases)
{
    var before = JsonSerializer.Serialize(item, options);
    var reference = engine.Calculate(item.Request, item.Pipeline);
    if (reference.IsFailure) throw new InvalidDataException(reference.Error);
    for (var i = 0; i < 20; i++) engine.Calculate(item.Request, item.Pipeline);
    var samples = new List<double>();
    for (var i = 0; i < 200; i++)
    {
        var start = Stopwatch.GetTimestamp();
        var result = engine.Calculate(item.Request, item.Pipeline);
        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (result.IsFailure || result.Value.Fingerprint != reference.Value.Fingerprint)
            throw new InvalidDataException("Repeated calculation diverged");
    }
    if (before != JsonSerializer.Serialize(item, options)) throw new InvalidDataException("Input was mutated");
    samples.Sort();
    double Percentile(double p) => samples[(int)Math.Ceiling(p * samples.Count) - 1];
    Console.WriteLine(JsonSerializer.Serialize(new { item.Name, runtime = Environment.Version.ToString(), samples = samples.Count,
        warmup = 20, p50Ms = Percentile(.5), p95Ms = Percentile(.95), p99Ms = Percentile(.99),
        value = reference.Value.Value, fingerprint = reference.Value.Fingerprint,
        scope = "numeric reducer including trace and fingerprint; excludes resolver, HTTP and persistence" }, options));
}
sealed record BenchmarkCase(string Name, CalculationRequest Request, CalculationPipelineDefinition Pipeline);
