using Xunit;

namespace Core.Tests.Architecture;

public sealed class DeterminismArchitectureTests
{
    private static readonly string[] AmbientSources =
    {
        "Guid.NewGuid(",
        "DateTime.UtcNow",
        "DateTime.Now",
        "Random.Shared",
        "new Random("
    };

    [Fact]
    public void Core_AmbientNondeterminism_IsExplicitlyClassifiedAtBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreRoot = Path.Combine(repositoryRoot, "src", "Core");
        var violations = Directory
            .EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path),
                Line = line,
                Number = index + 1
            }))
            .Where(candidate => AmbientSources.Any(candidate.Line.Contains))
            .Where(candidate => !candidate.Line.Contains(
                "nondeterministic-boundary:",
                StringComparison.Ordinal))
            .Select(candidate => $"{candidate.Path}:{candidate.Number}: {candidate.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Ambient nondeterminism must be removed or marked as an explicit boundary:\n"
            + string.Join("\n", violations));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Core")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the HeroScript repository root");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
