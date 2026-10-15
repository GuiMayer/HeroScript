using System.Text.Json;
using Xunit;

namespace API.Tests;

[Trait("Category", "Contract")]
public sealed class ContractDocumentationTests
{
    [Fact]
    public async Task LiveOpenApiCanGenerateTheSessionContract()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/runs/{runId}/session", out _));
        using var capabilities = await client.GetAsync("/api/v1/capabilities");
        using var payload = JsonDocument.Parse(await capabilities.Content.ReadAsStringAsync());
        Assert.Contains(payload.RootElement.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetString() == "compact-interaction-sessions-v1");
    }

    [Fact]
    public void CompactSessionContractDeclaresAtomicVersionAndConditionalReads()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var root = document.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/v1/runs/{runId}/session").GetProperty("get");
        Assert.Equal("stable", operation.GetProperty("x-heroscript-stability").GetString());
        foreach (var status in new[] { "200", "304", "409" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        var parameters = operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString());
        Assert.Contains("expectedSequence", parameters);
        Assert.Contains("If-None-Match", parameters);
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var sections = schemas.GetProperty("InteractionSession").GetProperty("properties");
        foreach (var section in new[] { "version", "run", "combat", "cardPresentations", "actions", "attributes" })
            Assert.True(sections.TryGetProperty(section, out _));
        Assert.True(schemas.GetProperty("InteractionVersion").GetProperty("properties").TryGetProperty("stateFingerprint", out _));
    }

    [Fact]
    public void CardPresentationContractSeparatesLayersAndDeclaresModifierQueries()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var root = document.RootElement;
        foreach (var path in new[] { "/api/v1/runs/{runId}/cards/presentations", "/api/v1/runs/{runId}/card-modifier-items",
                     "/api/v1/runs/{runId}/card-modifier-items/{itemInstanceId}/preview/{cardInstanceId}" })
            Assert.True(root.GetProperty("paths").GetProperty(path).TryGetProperty("get", out _));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var layers = schemas.GetProperty("CardNumericLayers").GetProperty("properties");
        Assert.True(layers.TryGetProperty("original", out _));
        Assert.True(layers.TryGetProperty("composedBase", out _));
        Assert.True(layers.TryGetProperty("source", out _));
        Assert.False(layers.TryGetProperty("impact", out _));
        Assert.Equal(512, schemas.GetProperty("CardPresentationBatch").GetProperty("properties").GetProperty("cards").GetProperty("maxItems").GetInt32());
    }
    [Fact]
    public void CreationContract_DeclaresRetryInputsButNotClientEligibility()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("RunStartRequest");
        Assert.Equal(new[] { "settingId", "contentRevision", "modeId" },
            request.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        var properties = request.GetProperty("properties");
        Assert.Equal("uuid", properties.GetProperty("commandId").GetProperty("format").GetString());
        foreach (var forbidden in new[] { "configName", "eligibility", "capturedEligibility", "unlockIds" })
            Assert.False(properties.TryGetProperty(forbidden, out _));
        Assert.False(properties.GetProperty("modeId").TryGetProperty("default", out _));
        var readModel = schemas.GetProperty("RunReadModel").GetProperty("properties");
        Assert.True(readModel.TryGetProperty("characterResources", out _));
        Assert.True(readModel.TryGetProperty("runResourceOwnerId", out _));
        var provenance = readModel.GetProperty("progressProvenance");
        Assert.True(provenance.GetProperty("nullable").GetBoolean());
        Assert.Equal(new[] { "Published", "Experimental", "Sandbox", "DevModder", "Branch" },
            provenance.GetProperty("enum").EnumerateArray().Select(item => item.GetString()));
        Assert.True(schemas.GetProperty("PlayerProfileProjection").GetProperty("properties").TryGetProperty("availability", out _));
        var probability = schemas.GetProperty("RandomImpactPreview").GetProperty("properties");
        foreach (var field in new[] { "probabilityUnit", "probabilitySource", "probabilityCalculation", "contentRevision", "snapshotHash" })
            Assert.True(probability.TryGetProperty(field, out _));
        Assert.Equal("#/components/schemas/RunEligibilitySnapshot",
            readModel.GetProperty("eligibility").GetProperty("allOf")[0].GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/RunCreationReceiptIdentity",
            readModel.GetProperty("creationReceipt").GetProperty("allOf")[0].GetProperty("$ref").GetString());
    }

    [Fact]
    public void Profiles_RequireExplicitSettingOnEverySurface()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var root = document.RootElement;
        var parameter = root.GetProperty("components").GetProperty("parameters").GetProperty("ProfileSetting");
        Assert.Equal("settingId", parameter.GetProperty("name").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        foreach (var suffix in new[] { "", "/stats", "/unlocks", "/achievements", "/runs" })
        {
            var operation = root.GetProperty("paths").GetProperty("/api/v1/profiles/{playerId}" + suffix).GetProperty("get");
            Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.TryGetProperty("$ref", out var reference) && reference.GetString() == "#/components/parameters/ProfileSetting");
        }
    }

    [Fact]
    public void OpenApiContract_DeclaresCriticalDeterministicRoutesAndProblems()
    {
        using var document = LoadJson("openapi", "heroscript-v1.json");
        var root = document.RootElement;

        Assert.Equal("3.0.4", root.GetProperty("openapi").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());
        AssertOperation(root.GetProperty("paths"), "/api/v1/runs/{runId}/character-attributes", "get", "experimental");
        AssertOperation(root.GetProperty("paths"), "/api/v1/combats/{combatId}/actors/{actorId}/attributes", "get", "experimental");

        var paths = root.GetProperty("paths");
        AssertOperation(paths, "/api/v1/content/settings", "get", "stable");
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
        var cardInspectionProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CardInspectionResult").GetProperty("properties");
        Assert.True(cardInspectionProperties.TryGetProperty("previewSteps", out _));
        Assert.True(cardInspectionProperties.TryGetProperty("isInPlayableZone", out _));
        Assert.False(cardInspectionProperties.TryGetProperty("isInHand", out _));
        var scenarioProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CombatScenarioDefinition").GetProperty("properties");
        Assert.True(scenarioProperties.TryGetProperty("startingCards", out _));
        Assert.False(scenarioProperties.TryGetProperty("deck", out _));
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/v1/runs/{runId}/card-zones", out _));
        var runReadProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("RunReadModel").GetProperty("properties");
        Assert.True(runReadProperties.TryGetProperty("cardZones", out _));
        Assert.False(runReadProperties.TryGetProperty("deck", out _));
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
        var sandboxProperties = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("SandboxCombatSnapshot").GetProperty("properties");
        Assert.True(sandboxProperties.TryGetProperty("playableCards", out _));
        Assert.False(sandboxProperties.TryGetProperty("hand", out _));
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
            "docs/api/interactive-session.md",
            "docs/api/getting-started.md",
            "docs/api/contracts.md",
            "docs/api/combat-sandbox.md",
            "docs/api/events.md",
            "docs/api/content-and-platform.md",
            "docs/api/changelog.md",
            "docs/content/packages-and-settings.md",
            "docs/content/run-eligibility.md",
            "docs/architecture/cross-cutting-systems.md",
            "docs/systems/calculations/calculation-system.md",
            "docs/systems/effects/effect-system.md",
            "docs/systems/modifiers/modifier-system.md",
            "docs/systems/relics/relic-system.md",
            "docs/systems/status/status-system.md",
            "examples/http/deterministic-run.http",
            "examples/http/interactive-session.http",
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
