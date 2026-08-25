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
        AssertOperation(paths, "/api/v1/runs/{runId}/checkpoints", "get", "stable");
        AssertOperation(paths, "/api/v1/runs/{runId}/timeline", "get", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/branches", "post", "experimental");
        AssertOperation(paths, "/api/v1/runs/{runId}/verify", "post", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/journal", "get", "stable");
        AssertOperation(paths, "/api/v1/combats/{combatId}/history", "get", "stable");
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

        var commandType = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("CommandEnvelope").GetProperty("properties").GetProperty("type");
        Assert.Contains("ACTIVATE_CONTENT_REVISION", commandType.GetProperty("description").GetString());

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
            "examples/http/deterministic-run.http",
            "examples/http/replay-and-events.http",
            "examples/http/content-publication.http",
            "examples/http/combat-sandbox.http"
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
