using System.Text.Json;
using Xunit;

namespace API.Tests;

[Trait("Category", "Contract")]
public sealed class ContractDocumentationTests
{
    [Fact]
    public void OpenApiContract_DeclaresCriticalDeterministicRoutesAndProblems()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var root = document.RootElement;

        Assert.Equal("3.0.4", root.GetProperty("openapi").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var paths = root.GetProperty("paths");
        AssertOperation(paths, "/api/v1/runs", "post", "stable");
        AssertOperation(paths, "/api/v1/runs/{runId}/commands", "post", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/commands", "post", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/resolutions/{commandId}", "get", "stable");
        AssertOperation(paths, "/api/v1/runs/{runId}/commits", "get", "stable");
        AssertOperation(paths, "/api/v1/runs/{runId}/commits/{sequence}", "get", "stable");
        AssertOperation(paths, "/api/v1/runs/{runId}/timeline", "get", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/branches", "post", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/verify", "post", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/journal", "get", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/history", "get", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation", "get", "experimental");
        AssertOperation(paths, "/api/v1/combats/{combatId}/cards/evaluations", "get", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/events/stream", "get", "stable");
        AssertOperation(paths, "/api/v1/admin/content/drafts", "post", "admin");
        AssertOperation(paths, "/api/v1/sandbox/scenarios/validate", "post", "experimental");
        AssertOperation(paths, "/api/v1/sandbox/runs", "post", "experimental");
        AssertOperation(paths, "/api/v1/sandbox/runs/{runId}/snapshot", "get", "experimental");
        AssertOperation(paths, "/api/v1/combats/{combatId}/timeline", "get", "experimental");
        AssertOperation(paths, "/api/v1/combats/{combatId}/timeline/{sequence}/branches", "post", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/branch-tree", "get", "experimental");
        AssertOperation(paths, "/api/v1/simulations", "post", "experimental");
        AssertOperation(paths, "/api/v1/simulations/{simulationId}", "get", "experimental");
        AssertOperation(paths, "/api/v1/simulations/{simulationId}/result", "get", "experimental");
        Assert.False(paths.TryGetProperty("/api/v1/combats/{combatId}/stack", out _));
        Assert.False(paths.TryGetProperty("/api/v1/entities/definitions", out _));
        Assert.False(paths.TryGetProperty("/api/v1/runs/{runId}/checkpoints", out _));

        var commandType = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CommandEnvelope").GetProperty("properties").GetProperty("type");
        Assert.Contains("ACTIVATE_CONTENT_REVISION", commandType.GetProperty("description").GetString());

        var resolution = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CombatResolutionRecord");
        Assert.Contains(
            "CompactWithSnapshotLookup",
            resolution.GetProperty("properties").GetProperty("mode").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()));
        var resolutionProperties = resolution.GetProperty("properties");
        Assert.True(resolutionProperties.TryGetProperty("rootSequence", out _));
        Assert.False(resolutionProperties.TryGetProperty("firstSequence", out _));
        Assert.False(resolutionProperties.TryGetProperty("finalSequence", out _));
        Assert.True(resolutionProperties.TryGetProperty("initialCombatStateHash", out _));
        Assert.True(resolutionProperties.TryGetProperty("finalCombatStateHash", out _));
        Assert.True(resolutionProperties.TryGetProperty("resolutionFingerprint", out _));
        var frameProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CombatAnimationFrame").GetProperty("properties");
        Assert.True(frameProperties.TryGetProperty("snapshotSequence", out _));
        Assert.True(frameProperties.TryGetProperty("effectSteps", out _));
        Assert.True(frameProperties.TryGetProperty("calculations", out _));
        Assert.True(frameProperties.TryGetProperty("applications", out _));
        Assert.True(frameProperties.TryGetProperty("cardZoneSteps", out _));
        var zoneGameplayPayload = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("GameplayCardZoneFlowPayload").GetProperty("properties");
        Assert.True(zoneGameplayPayload.TryGetProperty("flowId", out _));
        Assert.True(zoneGameplayPayload.TryGetProperty("requestedCount", out _));
        Assert.False(zoneGameplayPayload.TryGetProperty("cardDefinitionIds", out _));
        Assert.True(root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CardInspectionResult").GetProperty("properties")
            .TryGetProperty("previewSteps", out _));
        var timelineProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CombatTimelineItem").GetProperty("properties");
        Assert.True(timelineProperties.TryGetProperty("resolutionCommandId", out _));
        Assert.True(timelineProperties.TryGetProperty("stateAvailable", out _));
        Assert.True(timelineProperties.TryGetProperty("frames", out _));
        Assert.True(timelineProperties.TryGetProperty("facts", out _));
        Assert.False(timelineProperties.TryGetProperty("snapshotAvailable", out _));
        var combatSnapshotProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CombatStateSnapshot").GetProperty("properties");
        Assert.True(combatSnapshotProperties.TryGetProperty("actors", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("sides", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("relationships", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("phase", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("activation", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("priorityWindow", out _));
        Assert.True(combatSnapshotProperties.TryGetProperty("pendingActions", out _));
        Assert.True(root.GetProperty("components").GetProperty("schemas").TryGetProperty("RunLineage", out _));
        Assert.True(root.GetProperty("components").GetProperty("schemas").TryGetProperty("RunBranchTreeNode", out _));
        Assert.Equal(
            "#/components/schemas/SandboxCombatSnapshot",
            paths.GetProperty("/api/v1/sandbox/runs/{runId}/snapshot").GetProperty("get")
                .GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal(
            "#/components/schemas/SandboxCombatStateSnapshot",
            root.GetProperty("components").GetProperty("schemas")
                .GetProperty("SandboxCombatSnapshot").GetProperty("properties")
                .GetProperty("combat").GetProperty("$ref").GetString());
        var legalCandidateProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("LegalActionCandidate").GetProperty("properties");
        Assert.True(legalCandidateProperties.TryGetProperty("reactionTransition", out _));
        Assert.True(legalCandidateProperties.TryGetProperty("pendingAction", out _));
        Assert.Equal("#/components/schemas/ResolvedCardCost",
            legalCandidateProperties.GetProperty("costs").GetProperty("items").GetProperty("$ref").GetString());
        var applicationProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("EffectApplicationRecord").GetProperty("properties");
        Assert.True(applicationProperties.TryGetProperty("removedModifierInstanceIds", out _));
        Assert.Equal(
            "#/components/schemas/ModifierStackApplicationRecord",
            applicationProperties.GetProperty("modifierStackChanges").GetProperty("items")
                .GetProperty("$ref").GetString());
        Assert.Equal(
            "#/components/schemas/CombatResolutionRecord",
            root.GetProperty("components").GetProperty("schemas")
                .GetProperty("CombatCommandState").GetProperty("properties")
                .GetProperty("resolution").GetProperty("$ref").GetString());

        var responses = root.GetProperty("components").GetProperty("responses");
        Assert.True(responses.TryGetProperty("Problem", out var problem));
        Assert.True(problem.GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public void AsyncApiContract_DeclaresResumableRunAndCombatStreams()
    {
        using var document = LoadJson("asyncapi", "heroscript-events-v1.json");
        var root = document.RootElement;

        Assert.Equal("3.0.0", root.GetProperty("asyncapi").GetString());
        var channels = root.GetProperty("channels");
        Assert.Equal("/api/v1/runs/{runId}/events/stream", channels.GetProperty("runEvents").GetProperty("address").GetString());
        Assert.Equal("/api/v1/combats/{combatId}/events/stream", channels.GetProperty("combatEvents").GetProperty("address").GetString());

        var properties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("RunProjectionEvent").GetProperty("properties");
        Assert.True(properties.TryGetProperty("sequence", out _));
        Assert.True(properties.TryGetProperty("step", out _));
        Assert.True(properties.TryGetProperty("stateHash", out _));
        Assert.True(properties.TryGetProperty("correlationId", out _));
        Assert.True(properties.TryGetProperty("contentRevision", out _));
        Assert.True(properties.TryGetProperty("seed", out _));
        Assert.True(properties.TryGetProperty("commandPayloadHash", out _));
    }

    [Fact]
    public void ApiGuidesAndExamples_ArePresent()
    {
        var root = FindProjectRoot();
        var files = new[]
        {
            "docs/api/README.md",
            "docs/api/getting-started.md",
            "docs/api/contracts.md",
            "docs/api/combat-sandbox.md",
            "docs/api/events.md",
            "docs/api/content-and-platform.md",
            "docs/api/changelog.md",
            "docs/content/packages-and-settings.md",
            "docs/architecture/cross-cutting-systems.md",
            "docs/systems/calculations/calculation-system.md",
            "docs/systems/effects/effect-system.md",
            "docs/systems/modifiers/modifier-system.md",
            "docs/systems/relics/relic-system.md",
            "docs/systems/status/status-system.md",
            "examples/http/deterministic-run.http",
            "examples/http/replay-and-events.http",
            "examples/http/content-publication.http",
            "examples/http/combat-sandbox.http",
            "examples/godot-combat-client/addons/heroscript/HeroScriptClient.gd",
            "examples/godot-combat-client/README.md"
        };

        foreach (var relativePath in files)
            Assert.True(File.Exists(Path.Combine(root, relativePath)), $"Missing documentation artifact: {relativePath}");
    }

    private static void AssertOperation(JsonElement paths, string path, string verb, string stability)
    {
        var operation = paths.GetProperty(path).GetProperty(verb);
        Assert.Equal(stability, operation.GetProperty("x-heroscript-stability").GetString());
    }

    private static JsonDocument LoadJson(params string[] relativeParts) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(new[] { FindProjectRoot() }.Concat(relativeParts).ToArray())));

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
