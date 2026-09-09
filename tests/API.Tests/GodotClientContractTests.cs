using Xunit;

namespace API.Tests;

[Trait("Category", "Contract")]
public sealed class GodotClientContractTests
{
    [Fact]
    public void PackagedClient_UsesCanonicalRestAndSnapshotDrivenInput()
    {
        var path = Path.Combine(
            FindProjectRoot(), "examples", "godot-combat-client", "addons", "heroscript", "HeroScriptClient.gd");
        var source = File.ReadAllText(path);

        Assert.Contains("/api/v1/sandbox/runs", source, StringComparison.Ordinal);
        Assert.Contains("/legal-actions?actorId=", source, StringComparison.Ordinal);
        Assert.Contains("PLAY_CARD", source, StringComparison.Ordinal);
        Assert.Contains("PASS_PRIORITY", source, StringComparison.Ordinal);
        Assert.Contains("priorityWindow", source, StringComparison.Ordinal);
        Assert.Contains("waitingForInput", source, StringComparison.Ordinal);
        Assert.Contains("frame_ready", source, StringComparison.Ordinal);
        Assert.Contains("runSequence", source, StringComparison.Ordinal);
        Assert.Contains("frameIndex", source, StringComparison.Ordinal);
        Assert.Contains("/branch-tree", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EXECUTE_ACTION", source, StringComparison.Ordinal);
        Assert.DoesNotContain("heroId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("enemyIds", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 12 && directory != null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HeroScript.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("HeroScript repository root was not found.");
    }
}
