using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class CrossProcessFoundationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task IndependentProcessesAgreeOnIntentsCriticalInputsStackPayloadsReceiptsCommitsAndReplay()
    {
        var repository = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "HeroScript.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.Combine(repository.FullName, "tests", "Determinism.Probe", "bin", configuration, "net10.0", "Determinism.Probe.dll");
        Assert.True(File.Exists(probe), "Build HeroScript.slnx first; the cross-process probe is a solution test dependency.");
        string? baseline = null;
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = repository.FullName,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(probe);
            start.Environment["HERESCRIPT_REPO_ROOT"] = repository.FullName;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var text = await stdout; var error = await stderr;
            Assert.True(process.ExitCode == 0, $"Probe exited {process.ExitCode}: {text}\n{error}");
            var result = Assert.Single(text.Split('\n'), line => line.StartsWith("HEROSCRIPT_PROBE:", StringComparison.Ordinal))["HEROSCRIPT_PROBE:".Length..].Trim();
            using var json = JsonDocument.Parse(result);
            Assert.Equal(18, json.RootElement.GetProperty("transcript").GetArrayLength());
            if (baseline == null) baseline = result; else Assert.Equal(baseline, result);
            output.WriteLine($"process={repetition + 1}; {result}");
        }
    }
}
