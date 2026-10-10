using System.Diagnostics;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Meta;
using Core.Run;
using Core.Tests.Run;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Persistence;

/// <summary>Measures cold/warm reconstruction at bounded history sizes; timing is evidence, not a flaky correctness gate.</summary>
public sealed class WorldOneProfileHistoryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(256)]
    public async Task ColdAndWarmHistoryMatchAuthoritativeProofsAndRemainPartitioned(int historySize)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HeroScript", "foundation-history-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var source = new FileRunCommitStore(directory, NullLogger.Instance);
            for (var index = 1; index <= historySize; index++)
            {
                var state = ProfileProgressTests.State(index, ProfileProgressTests.Policy(2));
                var player = index % 2 == 0 ? "player" : "other";
                var setting = index % 4 < 2 ? "setting" : "secondary";
                state = state with { PlayerEntityId = player, SettingId = setting,
                    Eligibility = RunEligibility.Capture(state.ResolvedMode!, player, setting, "revision", ProfileProgressSnapshot.Empty(player, setting)).Value };
                await source.AppendAsync(ProfileProgressRunCommitStoreTests.Commit(state, command: index));
            }
            var expected = await source.ReadAsync("player", "setting");
            using var cold = new FileRunCommitStore(directory, NullLogger.Instance);
            var timer = Stopwatch.StartNew();
            var rebuilt = await cold.ReadAsync("player", "setting");
            var coldMetaMs = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(CanonicalJson.ComputeHash(expected), CanonicalJson.ComputeHash(rebuilt));
            timer.Restart();
            var projection = await new PlayerProfileProjectionReader(cold).ReadAsync("player", "setting");
            var coldHistoryMs = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(historySize / 4, projection.TotalRuns);
            Assert.Equal(expected.Sequence, projection.ProgressSequence);
            Assert.Single(projection.UnlockProofs);
            var warmMeta = new List<double>(); var warmHistory = new List<double>();
            for (var sample = 0; sample < 5; sample++)
            {
                timer.Restart();
                Assert.Equal(expected.Revision, (await cold.ReadAsync("player", "setting")).Revision);
                warmMeta.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart();
                Assert.Equal(projection.Revision, (await new PlayerProfileProjectionReader(cold).ReadAsync("player", "setting")).Revision);
                warmHistory.Add(timer.Elapsed.TotalMilliseconds);
            }
            Assert.Empty((await cold.ReadAsync("player", "missing")).Grants);
            output.WriteLine($"history={historySize} matching={projection.TotalRuns}; meta cold={coldMetaMs:F2}ms warm-max={warmMeta.Max():F2}ms; profile cold={coldHistoryMs:F2}ms warm-max={warmHistory.Max():F2}ms; revision={projection.ProgressRevision}");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
