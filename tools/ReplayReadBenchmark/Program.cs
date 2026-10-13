using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Projections;

if (args.Length < 2) throw new ArgumentException("Use <existing-store> <run-id>, or --fixture <new-directory> <count> [payload-kib]");
string path;
Guid runId;
if (args[0] == "--fixture")
{
    path = Path.GetFullPath(args[1]);
    if (Directory.Exists(path)) throw new ArgumentException("Fixture directory must not exist; no existing data is overwritten.");
    var count = args.Length > 2 ? int.Parse(args[2]) : 125;
    var size = args.Length > 3 ? int.Parse(args[3]) : 16;
    if (count is < 1 or > 10000 || size is < 0 or > 1024) throw new ArgumentOutOfRangeException();
    runId = DeterministicId.Create(17, (ulong)count, "replay-read-fixture");
    var directory = Path.Combine(path, runId.ToString("D"), "commits");
    Directory.CreateDirectory(directory);
    RunState? previous = null;
    var context = DeterministicContext.Create(17, "diagnostic-fixture");
    for (var sequence = 1; sequence <= count; sequence++)
    {
        context = context.AdvanceStep();
        var payload = JsonSerializer.SerializeToElement(new { sequence });
        var state = new RunState { RunId = runId, Sequence = sequence, Lineage = RunLineage.Root(runId),
            Determinism = context, Metadata = new Dictionary<string, JsonElement> {
                ["blob"] = JsonSerializer.SerializeToElement(new string('x', size * 1024)),
                ["counter"] = payload }.ToImmutableDictionary() };
        var hash = CanonicalJson.ComputeHash(state);
        var commit = new RunCommit { RunId = runId, Sequence = sequence,
            RootCommand = new(DeterministicId.Create(17, (ulong)sequence, "diagnostic-command"), "DIAGNOSTIC", sequence-1, context.Step-1, CanonicalJson.ComputeHash(payload)),
            Command = payload, PreviousStateHash = previous == null ? "" : CanonicalJson.ComputeHash(previous),
            StateHash = hash, StateAfter = state, BeforeStep = context.Step-1, AfterStep = context.Step,
            Lineage = sequence == 1 ? state.Lineage : null,
            Frames = [new() { FrameIndex = 0, Step = context.Step, Kind = "diagnostic",
                ResultHash = hash, Resolution = JsonSerializer.SerializeToElement(new { detail = new string('y', size * 1024) }) }] };
        commit = commit with { Facts = RunCommitFacts.FromFrames(commit.Frames) };
        var prepared = PreparedRunCommit.CreateVerified(commit, hash, previous);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{sequence:D8}.json"), prepared.Bytes.ToArray());
        previous = state;
    }
}
else { path = Path.GetFullPath(args[0]); runId = Guid.Parse(args[1]); if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path); }

Console.WriteLine(JsonSerializer.Serialize(new { kind = "environment", path, runId, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount }));
foreach (var mode in new[] { "materialized", "envelopes" })
foreach (var capacity in new[] { 0, 256 })
{
    using var store = new FileRunCommitStore(path, NullLogger.Instance, capacity);
    for (var sample = 0; sample < 4; sample++)
    {
        var allocated = GC.GetTotalAllocatedBytes();
        var timer = Stopwatch.StartNew();
        var commits = mode == "materialized" ? await store.LoadCommitsAsync(runId, 0, 25) :
            (await store.LoadCommitEnvelopesAsync(runId)).Take(25).ToArray();
        var readMs = timer.Elapsed.TotalMilliseconds;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(commits.Select(c => new {
            c.Sequence, c.AfterStep, c.RootCommand.Type, c.StateHash, frames = c.Frames }), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Console.WriteLine(JsonSerializer.Serialize(new { mode, capacity, sample, firstRead = sample == 0,
            count = commits.Count, readMs, totalMs = timer.Elapsed.TotalMilliseconds, payloadBytes = bytes.Length,
            allocatedBytes = GC.GetTotalAllocatedBytes() - allocated }));
    }
}
