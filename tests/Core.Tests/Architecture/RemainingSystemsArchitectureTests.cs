using Core.Abstractions.Persistence;
using Core.Entity;
using Xunit;

namespace Core.Tests.Architecture;

/// <summary>
/// Migration fences for the remaining authority consolidation. Existing debt
/// is named explicitly and the allowlists may only shrink.
/// </summary>
public sealed class RemainingSystemsArchitectureTests
{
    [Fact]
    public void PersistenceAuthorities_CannotGrowBeyondMigrationAllowlist()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Core.Infrastructure.Persistence.FileRunCommitStore"
        };
        var implementations = typeof(IRunCommitStore).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IRunCommitStore).IsAssignableFrom(type))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            implementations.IsSubsetOf(allowed),
            $"A new gameplay persistence authority was introduced: {string.Join(", ", implementations.Except(allowed))}");
    }

    [Fact]
    public void MutableComponents_CannotGrowBeyondMigrationAllowlist()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Core.Entity.Components.InventoryComponent",
            "Core.Entity.Components.ResourceComponent",
            "Core.Entity.Components.StatsComponent"
        };
        var implementations = typeof(IComponent).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            implementations.IsSubsetOf(allowed),
            $"A new mutable component was introduced: {string.Join(", ", implementations.Except(allowed))}");
    }

    [Fact]
    public void PureReducers_DoNotReadTheFilesystem()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreRoot = Path.Combine(repositoryRoot, "src", "Core");
        var forbidden = new[] { "File.", "Directory.", "FileStream", "PhysicalFile" };
        var violations = Directory
            .EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith("Reducer", StringComparison.Ordinal)
                           || Path.GetFileNameWithoutExtension(path).EndsWith("Transitions", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path),
                Line = line,
                Number = index + 1
            }))
            .Where(candidate => forbidden.Any(candidate.Line.Contains))
            .Select(candidate => $"{candidate.Path}:{candidate.Number}: {candidate.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Pure reducers must receive all inputs explicitly:\n" + string.Join("\n", violations));
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
}
