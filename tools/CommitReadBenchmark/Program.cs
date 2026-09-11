using System.Diagnostics;
using System.Text.Json;
using Core.Infrastructure.Persistence;
using Core.Logging;

if (args.Length != 2 || !Guid.TryParse(args[1], out var runId))
    throw new ArgumentException("Usage: CommitReadBenchmark <existing-store-directory> <run-id>");
if (!Directory.Exists(args[0])) throw new DirectoryNotFoundException(args[0]);
var missingCommandId = Guid.NewGuid();
foreach (var lookup in new[] { false, true })
foreach (var capacity in new[] { 0, 256 })
{
    using var store = new FileRunCommitStore(args[0], NullLogger.Instance, capacity);
    var samples = new List<double>();
    var count = 0;
    for (var iteration = 0; iteration < 6; iteration++)
    {
        var clock = Stopwatch.StartNew();
        if (lookup)
        {
            await store.FindCommandAsync(runId, missingCommandId);
            count = (await store.ListCommitSequencesAsync(runId)).Count;
        }
        else
        {
            var commits = await store.LoadCommitsAsync(runId);
            count = commits.Count;
        }
        clock.Stop();
        if (iteration > 0) samples.Add(clock.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine(JsonSerializer.Serialize(new {
        lookupOnly = lookup, validationCacheCapacity = capacity, commits = count,
        medianMs = samples.Order().ElementAt(samples.Count / 2), samplesMs = samples
    }));
}
