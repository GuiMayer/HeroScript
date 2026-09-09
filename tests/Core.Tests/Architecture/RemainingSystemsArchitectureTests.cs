using Core.Abstractions.Persistence;
using System.Text.RegularExpressions;
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
    public void MutableEntityComponentAuthority_IsRemoved()
    {
        var removed = new[]
        {
            "Core.Entity.Components.InventoryComponent",
            "Core.Entity.Components.ResourceComponent",
            "Core.Entity.Components.StatsComponent",
            "Core.Entity.IComponent",
            "Core.Entity.ComponentBase"
        };
        var present = typeof(Core.Combat.Models.EntityState).Assembly.GetTypes()
            .Where(type => type.FullName != null && removed.Contains(type.FullName, StringComparer.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(present);
    }

    [Fact]
    public void GameplaySource_UsesOnlyTheGenericImmutableActorModel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var forbidden = new[]
        {
            @"\.Hero\b",
            @"\.Enemies\b",
            @"\bIsHero\b",
            @"\bEntityCombatAdapter\b",
            @"\bUpdateEntityFromCombat\b",
            @"\bCore\.Entity\.Entity\b",
            @"\bIComponent\b",
            @"\b(?:ResourceComponent|StatsComponent|InventoryComponent)\b"
        }.Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant)).ToArray();
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path),
                Line = line,
                Number = index + 1
            }))
            .Where(candidate => forbidden.Any(pattern => pattern.IsMatch(candidate.Line)))
            .Select(candidate => $"{candidate.Path}:{candidate.Number}: {candidate.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Gameplay source bypasses the generic immutable actor model:\n" + string.Join("\n", violations));
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

    [Fact]
    public void GameplaySource_DoesNotUsePermissiveOrLegacyContentPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var forbidden = new[]
        {
            "strictMode: false",
            "\"Entities/",
            "\"Gambits/",
            "\"Modifiers/",
            "\"StatusEffects/",
            "\"Pipelines/",
            "BaseDefinitionId"
        };
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
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
            "Gameplay content must use the strict canonical registry:\n" + string.Join("\n", violations));

        var resourcesRoot = Path.Combine(repositoryRoot, "data", "configs", "default", "Resources");
        var directoryNames = Directory.EnumerateDirectories(resourcesRoot)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Entities", directoryNames);
        Assert.DoesNotContain("Gambits", directoryNames);
        Assert.DoesNotContain("Modifiers", directoryNames);
        Assert.DoesNotContain("StatusEffects", directoryNames);
        Assert.DoesNotContain("Pipelines", directoryNames);
        Assert.Contains("entities", directoryNames);
        Assert.Contains("gambits", directoryNames);
        Assert.Contains("modifiers", directoryNames);
        Assert.Contains("status-effects", directoryNames);
    }

    [Fact]
    public void CombatFlowComposition_HasFocusedDependencyBoundaries()
    {
        AssertFieldTypes(
            typeof(Core.Combat.Flow.CombatFlowPlanner),
            typeof(Core.Content.IContentRuntimeResolver),
            typeof(Core.Combat.Flow.ICombatBoundaryExecutor),
            typeof(Core.Combat.Intents.IIntentResolver));
        AssertFieldTypes(
            typeof(Core.Combat.Flow.CombatCommandHandler),
            typeof(Core.Combat.LegalActions.ILegalActionResolver),
            typeof(Core.Combat.Flow.ICombatActionStateReducer));
        AssertFieldTypes(
            typeof(Core.Combat.Flow.AutomaticFlowDriver),
            typeof(Core.Combat.Flow.ICombatFlowPlanner),
            typeof(Core.Combat.Gambits.IDecisionPolicyRegistry),
            typeof(Core.Combat.Flow.ICombatCommandHandler));

        var boundaryDependencies = FieldTypes(typeof(Core.Combat.Flow.CombatBoundaryExecutor));
        Assert.DoesNotContain(typeof(Core.Content.IContentRuntimeResolver), boundaryDependencies);
        Assert.DoesNotContain(typeof(Core.Combat.Intents.IIntentResolver), boundaryDependencies);
        Assert.DoesNotContain(typeof(Core.Combat.Gambits.IDecisionPolicyRegistry), boundaryDependencies);
        Assert.DoesNotContain(typeof(Core.Combat.LegalActions.ILegalActionResolver), boundaryDependencies);
        Assert.DoesNotContain(typeof(Core.Run.IRunCombatResolutionCommitter), boundaryDependencies);

        var coordinatorDependencies = FieldTypes(typeof(Core.Combat.CombatRunCoordinator));
        Assert.Contains(typeof(Core.Combat.Flow.ICombatCommandHandler), coordinatorDependencies);
        Assert.Contains(typeof(Core.Combat.Flow.IAutomaticFlowDriver), coordinatorDependencies);
        Assert.DoesNotContain(typeof(Core.Combat.LegalActions.ILegalActionResolver), coordinatorDependencies);
        Assert.DoesNotContain(typeof(Core.Combat.Gambits.IDecisionPolicyRegistry), coordinatorDependencies);
    }

    [Fact]
    public void CombatFlowComposition_RemainsWithinArchitecturalSizeBudgets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var budgets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Core/Combat/CombatRunCoordinator.cs"] = 650,
            ["src/Core/Combat/Flow/CombatFlowPlanner.cs"] = 220,
            ["src/Core/Combat/Flow/CombatCommandHandler.cs"] = 320,
            ["src/Core/Combat/Flow/AutomaticFlowDriver.cs"] = 340,
            ["src/Core/Combat/Flow/CombatBoundaryExecutor.cs"] = 1100
        };
        var violations = budgets
            .Select(item => new
            {
                item.Key,
                Budget = item.Value,
                Lines = File.ReadLines(Path.Combine(
                    repositoryRoot,
                    item.Key.Replace('/', Path.DirectorySeparatorChar))).Count()
            })
            .Where(item => item.Lines > item.Budget)
            .Select(item => $"{item.Key}: {item.Lines} lines (budget {item.Budget})")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Combat composition services exceeded their size budgets:\n" + string.Join("\n", violations));

        var legacyStaticEntrypoints = typeof(Core.Combat.Flow.CombatFlowPlanner)
            .GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.DeclaredOnly);
        Assert.Empty(legacyStaticEntrypoints);
    }

    private static Type[] FieldTypes(Type type) => type
        .GetFields(System.Reflection.BindingFlags.Instance |
                   System.Reflection.BindingFlags.NonPublic |
                   System.Reflection.BindingFlags.DeclaredOnly)
        .Select(field => field.FieldType)
        .ToArray();

    private static void AssertFieldTypes(Type type, params Type[] expected)
    {
        var actual = FieldTypes(type);
        Assert.Equal(
            expected.OrderBy(item => item.FullName, StringComparer.Ordinal),
            actual.OrderBy(item => item.FullName, StringComparer.Ordinal));
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
