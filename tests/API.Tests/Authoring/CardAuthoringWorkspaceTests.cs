using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using API.Services;
using Core.Content;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mods;
using Xunit;

namespace API.Tests.Authoring;

public sealed class CardAuthoringWorkspaceTests(CardAuthoringWorkspaceTests.WorkspaceFactory factory)
    : IClassFixture<CardAuthoringWorkspaceTests.WorkspaceFactory>
{
    [Theory]
    [InlineData("uno-clash", "uno_red_0", "heroscript.uno-clash", "Resources/cards/uno.json", "circular-table", "t_red_0")]
    [InlineData("circular-table", "t_red_0", "heroscript.circular-table", "Resources/cards/table.json", "uno-clash", "uno_red_0")]
    public async Task RealSettingsExposeSeparatePublishedCardsAndSourceProvenance(
        string setting, string card, string package, string source, string otherSetting, string otherCard)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/settings/{setting}");
        request.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var compilation = await response.Content.ReadFromJsonAsync<SettingCompilation>();
        Assert.NotNull(compilation);
        Assert.Contains(compilation.Provenance, entry =>
            entry.Kind == "cards" && entry.DefinitionId == card && entry.PackageId == package &&
            entry.SourcePath == source && entry.Operation == PackageContentOperation.Definition);

        var revision = compilation.Bundle.Manifest.Revision;
        var definition = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/content/cards/{card}?configName={setting}&revision={revision}");
        Assert.Equal(card, definition.GetProperty("definition").GetProperty("cardId").GetString());

        using var wrongCard = await client.GetAsync($"/api/v1/content/cards/{otherCard}?configName={setting}&revision={revision}");
        using var wrongSetting = await client.GetAsync($"/api/v1/content/cards/{card}?configName={otherSetting}&revision={revision}");
        Assert.Equal(HttpStatusCode.NotFound, wrongCard.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongSetting.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash", "uno_red_0", "circular-table", "card.played.to-table", "table.played")]
    [InlineData("circular-table", "t_red_0", "uno-clash", "table.played", "card.played.to-table")]
    public async Task AuthoringApiReadsAndValidatesCardsWithoutPersisting(
        string setting, string cardId, string otherSetting, string destination, string otherDestination)
    {
        using var client = factory.CreateClient();
        var root = $"/api/v1/admin/authoring/settings/{setting}";
        using var unauthorized = await client.GetAsync(root + "/definitions/cards");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        client.DefaultRequestHeaders.Add("X-Admin-Key", "dev-admin-key");

        var discovered = await client.GetFromJsonAsync<CardAuthoringSettings>("/api/v1/admin/authoring/settings");
        Assert.Contains(discovered!.Items, item => item.SettingId == setting && item.WritablePackageId is not null);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/cards/schema");
        Assert.Equal("tags", schema!.References["/tags"]);
        Assert.Equal("status-effects", schema.References["/components/*/effect/statusId"]);
        Assert.Equal("resources", schema.References["/components/*/effect/targetResource"]);
        Assert.Equal("resources", schema.References["/components/*/effects/*/selectionResourceId"]);
        Assert.Equal("resources", schema.References["/components/*/effect/continuation/selectionResourceId"]);
        Assert.Equal("status-effects", schema.References["/components/*/effect/dispel/statusIds/*"]);
        Assert.Equal("resources", schema.References["/components/*/costs/alternativeCosts/*/costs/*/resourceId"]);
        Assert.Equal("resources", schema.References["/components/*/selectionResourceId"]);
        Assert.Equal("card-resolution-flows", schema.References["/components/*/cardZoneResolutionFlowId"]);
        var destinations = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/card-resolution-flows");
        Assert.Contains(destinations!.Items, item => item.Id == destination);
        Assert.DoesNotContain(destinations.Items, item => item.Id == otherDestination);
        var zones = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/card-zones?q=hand");
        Assert.Contains(zones!.Items, item => item.Id == "hand");
        var payments = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/card-payment-flows");
        Assert.DoesNotContain(payments!.Items, item => item.Id == destination);
        var statuses = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/status-effects");
        var resources = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/resources");
        var status = Assert.Single(statuses!.Items.Take(1)).Id;
        var resource = Assert.Single(resources!.Items.Take(1)).Id;
        var componentSchema = schema.Schema["properties"]!["components"]!["items"]!.AsObject();
        var variants = (componentSchema["anyOf"] ?? componentSchema["oneOf"])!.AsArray();
        Assert.Contains(variants, branch => (string?)branch?["properties"]?["type"]?["const"] == "effect");
        Assert.Contains(variants, branch => (string?)branch?["properties"]?["type"]?["const"] == "trigger");
        Assert.Contains("cardId", schema.Schema["required"]!.AsArray().Select(value => value!.GetValue<string>()));

        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/cards?limit=1");
        Assert.Single(page!.Items);
        Assert.NotNull(page.NextCursor);
        var cursor = Uri.EscapeDataString(page.NextCursor);
        var next = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(
            root + "/definitions/cards?limit=1&cursor=" + cursor);
        Assert.NotEqual(page.Items[0].DefinitionId, next!.Items[0].DefinitionId);
        using var wrongCursor = await client.GetAsync(
            $"/api/v1/admin/authoring/settings/{otherSetting}/definitions/cards?limit=1&cursor={cursor}");
        Assert.Equal(HttpStatusCode.BadRequest, wrongCursor.StatusCode);

        using var detailResponse = await client.GetAsync(root + "/definitions/cards/" + cardId);
        detailResponse.EnsureSuccessStatusCode();
        var version = detailResponse.Headers.ETag!.Tag;
        var detail = await detailResponse.Content.ReadFromJsonAsync<CardAuthoringDefinition>();
        Assert.True(detail!.Card.CanEdit);
        Assert.NotEmpty(detail.Provenance);
        var tag = detail.Effective.GetProperty("tags").EnumerateArray().First().GetString()!;
        var tags = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringTag>>(
            root + "/references/tags?q=" + Uri.EscapeDataString(tag) + "&limit=500");
        Assert.Contains(tags!.Items, item => item.Id == tag && item.DeclaredOnCard);
        var inherited = await client.GetFromJsonAsync<CardAuthoringDefinition>(root + "/definitions/cards/basic_attack");
        Assert.False(inherited!.Card.CanDelete);
        Assert.Equal("heroscript.base", inherited.Card.PackageId);

        var storedFiles = await ReadSnapshot(Path.Combine(factory.PersistenceRoot, "content"));
        var storedVersion = AuthoringSourceVersion.CreateETag(storedFiles);
        var sourceVersion = AuthoringSourceVersion.CreateETag(await ReadSnapshot(factory.PackageRoot));
        var activeBefore = await CurrentRevision(client, setting);

        var definition = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
        definition["tags"]!.AsArray().Add("authoring_preview_tag");
        var clone = definition.DeepClone().AsObject();
        clone["cardId"] = "authoring_preview_card";
        var components = clone["components"]!.AsArray();
        components.First(item => (string?)item?["type"] == "cost")!["costs"]!["alternativeCosts"] = new JsonArray(new JsonObject
        {
            ["optionId"] = "authoring_alternative",
            ["costs"] = new JsonArray(new JsonObject { ["resourceId"] = resource, ["amount"] = 1 })
        });
        components.First(item => (string?)item?["type"] == "condition")!["failureReason"] = "Test condition message";
        components.First(item => (string?)item?["type"] == "targeting")!["allowSelf"] = true;
        var disposition = components.First(item => (string?)item?["type"] == "disposition")!;
        disposition["cardZoneResolutionFlowId"] = destination;
        clone["components"]!.AsArray().Add(new JsonObject
        {
            ["type"] = "effect", ["componentId"] = "authoring_preview_effect",
            ["effect"] = new JsonObject
            {
                ["type"] = "APPLY_STATUS", ["target"] = "SELF", ["statusId"] = status,
                ["chainedEffects"] = new JsonArray(new JsonObject
                {
                    ["type"] = "MODIFY_RESOURCE", ["target"] = "SELF", ["targetResource"] = resource, ["flatValue"] = 1
                })
            }
        });
        var candidate = new CardAuthoringChangeRequest
        {
            ChangeId = Guid.NewGuid(),
            Operations =
            [
                Operation(CardAuthoringOperationType.Update, cardId, definition),
                Operation(CardAuthoringOperationType.Create, "authoring_preview_card", clone)
            ]
        };
        using var noVersion = await Preview(null, candidate);
        Assert.Equal((HttpStatusCode)428, noVersion.StatusCode);
        using var stale = await Preview("\"stale\"", candidate);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        using var preview = await Preview(version, candidate);
        preview.EnsureSuccessStatusCode();
        var validation = await preview.Content.ReadFromJsonAsync<CardAuthoringValidation>();
        Assert.True(validation!.IsValid);
        Assert.NotEqual(activeBefore, validation.CandidateRevisions[setting]);
        Assert.DoesNotContain(otherSetting, validation.CandidateRevisions.Keys);

        disposition["cardZoneResolutionFlowId"] = "unknown_authoring_destination";
        using var invalidDestination = await Preview(version, candidate with
        {
            Operations = [Operation(CardAuthoringOperationType.Create, "authoring_preview_card", clone)]
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidDestination.StatusCode);
        Assert.Contains("unknown_authoring_destination", await invalidDestination.Content.ReadAsStringAsync());

        definition["components"]!.AsArray()[0]!["type"] = "unknown-component";
        using var invalid = await Preview(version, candidate with
        {
            Operations = [Operation(CardAuthoringOperationType.Update, cardId, definition)]
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("validation").GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "CARD_SCHEMA_INVALID" &&
                diagnostic.GetProperty("definitionId").GetString() == cardId);

        clone["rarity"] = 999;
        using var invalidRarity = await Preview(version, candidate with
        {
            Operations = [Operation(CardAuthoringOperationType.Create, "authoring_preview_card", clone)]
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidRarity.StatusCode);
        var rarityProblem = await invalidRarity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(rarityProblem.GetProperty("validation").GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "CARD_RARITY_INVALID" &&
                diagnostic.GetProperty("jsonPointer").GetString() == "/rarity");

        using var inheritedDelete = await Preview(version, candidate with
        {
            Operations = [Operation(CardAuthoringOperationType.Delete, "basic_attack")]
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inheritedDelete.StatusCode);
        var rejected = await inheritedDelete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(rejected.GetProperty("validation").GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "CARD_READ_ONLY");

        Assert.Equal(activeBefore, await CurrentRevision(client, setting));
        Assert.Equal(storedVersion, AuthoringSourceVersion.CreateETag(
            await ReadSnapshot(Path.Combine(factory.PersistenceRoot, "content"))));
        Assert.Equal(sourceVersion, AuthoringSourceVersion.CreateETag(await ReadSnapshot(factory.PackageRoot)));

        async Task<HttpResponseMessage> Preview(string? etag, CardAuthoringChangeRequest body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, root + "/changes/validate")
            {
                Content = JsonContent.Create(body)
            };
            if (etag is not null)
                request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        }
    }

    private static async Task<string?> CurrentRevision(HttpClient client, string setting)
    {
        var content = await client.GetFromJsonAsync<JsonElement>($"/api/v1/content/revisions?configName={setting}");
        return content.GetProperty("currentRevision").GetString();
    }

    private static CardAuthoringOperation Operation(CardAuthoringOperationType type, string id, JsonNode? definition = null) =>
        new()
        {
            Operation = type, Kind = "cards", DefinitionId = id,
            Definition = definition is null ? null : JsonSerializer.SerializeToElement(definition)
        };

    [Fact]
    public async Task SourceChangeExpiresCursorAndAppearsInGeneratedContract()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "dev-admin-key");
        const string route = "/api/v1/admin/authoring/settings/uno-clash/definitions/cards";
        var before = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(route + "?limit=1");
        var file = Path.Combine(factory.PackageRoot, "uno-clash", "Resources", "cards", "uno.json");
        var original = await File.ReadAllBytesAsync(file);
        try
        {
            await File.AppendAllTextAsync(file, "\n");
            using var response = await client.GetAsync(route + "?limit=1&cursor=" + Uri.EscapeDataString(before!.NextCursor!));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.NotEqual(before.SourceVersion, response.Headers.ETag!.Tag);
        }
        finally
        {
            await File.WriteAllBytesAsync(file, original);
        }

        var contract = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = contract.GetProperty("paths");
        var preview = paths.GetProperty("/api/v1/admin/authoring/settings/{settingId}/changes/validate").GetProperty("post");
        Assert.True(preview.GetProperty("responses").TryGetProperty("422", out _));
        Assert.True(preview.GetProperty("responses").TryGetProperty("428", out _));
        foreach (var action in new[] { "undo", "redo" })
        {
            var operation = paths.GetProperty($"/api/v1/admin/authoring/settings/{{settingId}}/changes/{{changeId}}/{action}")
                .GetProperty("post");
            Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
        }

        var authoring = await client.GetFromJsonAsync<JsonElement>("/openapi/authoring-v1.json");
        var authoringPaths = authoring.GetProperty("paths");
        var prefix = "/api/v1/admin/authoring/";
        Assert.Equal(paths.EnumerateObject().Select(item => item.Name)
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal),
            authoringPaths.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        var operations = authoringPaths.EnumerateObject().SelectMany(item => item.Value.EnumerateObject()).ToArray();
        Assert.Equal(operations.Length, operations.Select(item => item.Value.GetProperty("operationId").GetString()).Distinct().Count());
        foreach (var operation in operations)
        {
            Assert.True(operation.Value.GetProperty("security")[0].TryGetProperty("AuthoringAdminKey", out _));
            if (operation.Name == "post")
                Assert.Contains(operation.Value.GetProperty("parameters").EnumerateArray(), parameter =>
                    parameter.GetProperty("name").GetString() == "If-Match" && parameter.GetProperty("required").GetBoolean());
        }
        Assert.Equal("X-Admin-Key", authoring.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("AuthoringAdminKey").GetProperty("name").GetString());
        Assert.Equal("object", authoring.GetProperty("components").GetProperty("schemas")
            .GetProperty("AuthoringKindSchema").GetProperty("properties").GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SourceVersionDetectsByteChangesEvenWhenCompilationDoesNot()
    {
        var root = Path.Combine(factory.PersistenceRoot, "source-version");
        WorkspaceFactory.CopyPackages(root);
        var compiler = new SettingCompiler([new DirectoryPackageProvider("version-test", root)]);
        var firstCompilation = await compiler.CompileAsync("uno-clash");
        Assert.True(firstCompilation.IsSuccess, firstCompilation.IsFailure ? firstCompilation.Error : null);

        var first = await ReadSnapshot(root);
        var version = AuthoringSourceVersion.CreateETag(first);
        Assert.Matches("^\"[0-9a-f]{64}\"$", version);
        Assert.Equal(version, AuthoringSourceVersion.CreateETag(first.Reverse().ToDictionary()));

        var path = Path.Combine(root, "uno-clash", "Resources", "cards", "uno.json");
        await File.AppendAllTextAsync(path, "\n");
        var secondCompilation = await compiler.CompileAsync("uno-clash");
        Assert.True(secondCompilation.IsSuccess, secondCompilation.IsFailure ? secondCompilation.Error : null);
        Assert.Equal(firstCompilation.Value.Bundle.Manifest.Revision, secondCompilation.Value.Bundle.Manifest.Revision);
        Assert.NotEqual(version, AuthoringSourceVersion.CreateETag(await ReadSnapshot(root)));
    }

    [Fact]
    public async Task SharedPackageFixtureExposesInheritedCardsAndBlocksDeletingAReferencedCard()
    {
        var root = Path.Combine(factory.PersistenceRoot, "shared-fixture");
        var shared = Path.Combine(root, "base");
        Directory.CreateDirectory(shared);
        await File.WriteAllTextAsync(Path.Combine(shared, "package.json"), """
            {
              "packageId": "fixture.base",
              "version": "1.0.0",
              "content": [
                { "operation": "definition", "kind": "cards", "sourcePath": "cards.json", "artifactPath": "cards/shared.json" },
                { "operation": "definition", "kind": "card-pools", "sourcePath": "pools.json", "artifactPath": "card-pools/shared.json" }
              ]
            }
            """);
        var cardsPath = Path.Combine(shared, "cards.json");
        const string retained = """
            {"cardId":"retained","tags":["existing"],"components":[{"type":"condition","componentId":"ready","expression":"1"}]}
            """;
        await File.WriteAllTextAsync(cardsPath, $$"""
            {
              "retained": {{retained}},
              "referenced": {"cardId":"referenced","components":[{"type":"condition","componentId":"ready","expression":"1"}]}
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(shared, "pools.json"),
            """{"shared":{"poolId":"shared","explicitCardIds":["referenced"]}}""");

        foreach (var setting in new[] { "fixture-a", "fixture-b" })
        {
            var package = Path.Combine(root, setting);
            var settings = Directory.CreateDirectory(Path.Combine(package, "settings"));
            await File.WriteAllTextAsync(Path.Combine(package, "package.json"), $$"""
                {
                  "packageId": "{{setting}}",
                  "version": "1.0.0",
                  "dependencies": [{ "packageId": "fixture.base", "versionRange": "1.0.0" }]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(settings.FullName, setting + ".json"), $$"""
                {
                  "settingId": "{{setting}}",
                  "packages": [{ "packageId": "{{(setting == "fixture-a" ? "fixture.base" : setting)}}", "versionRange": "1.0.0" }]
                }
                """);
        }

        var provider = new DirectoryPackageProvider("shared-fixture", root);
        var compiler = new SettingCompiler([provider]);
        var validator = new ContentGraphValidator();
        foreach (var setting in new[] { "fixture-a", "fixture-b" })
        {
            var compiled = await compiler.CompileAsync(setting);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
            Assert.Contains(compiled.Value.Provenance, item =>
                item.DefinitionId == "referenced" && item.Kind == "cards" && item.PackageId == "fixture.base");
            var validation = validator.Validate(compiled.Value.Bundle);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authoring:WritablePackages:fixture-a"] = "fixture.base"
        }).Build();
        var authoring = new CardAuthoringService([provider], compiler, ContentKindRegistry.Default,
            factory.Services.GetRequiredService<IContentPublicationService>(), config);
        var workspace = (await authoring.CaptureAsync(default)).Value;
        var owner = (await authoring.GetViewAsync(workspace, "fixture-a", default)).Value;
        var inheritor = (await authoring.GetViewAsync(workspace, "fixture-b", default)).Value;
        Assert.True(owner.Cards["referenced"].CanEdit);
        Assert.False(inheritor.Cards["referenced"].CanEdit);

        var change = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid() };
        var deleted = await authoring.ValidateAsync(workspace, owner, change with
        {
            Operations = [Operation(CardAuthoringOperationType.Delete, "referenced")]
        }, default);
        Assert.False(deleted.IsValid);
        var affected = new[] { "fixture-a", "fixture-b" };
        Assert.Equal(affected, deleted.Diagnostics.Where(item => item.Code == "CARD_GRAPH_INVALID" &&
                item.DefinitionId == "referenced" && item.Parameters["message"].Contains("card-pools/shared"))
            .Select(item => item.Parameters["settingId"]));

        var allowed = await authoring.ValidateAsync(workspace, owner, change with
        {
            Operations = [Operation(CardAuthoringOperationType.Delete, "retained")]
        }, default);
        Assert.True(allowed.IsValid);
        Assert.Equal(affected, allowed.CandidateRevisions.Keys);
        using var source = JsonDocument.Parse(await File.ReadAllBytesAsync(cardsPath));
        Assert.True(source.RootElement.TryGetProperty("retained", out _));
    }

    private static async Task<Dictionary<string, ReadOnlyMemory<byte>>> ReadSnapshot(string root)
    {
        var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), await File.ReadAllBytesAsync(path));
        return files;
    }

    public sealed class WorkspaceFactory : TestWebApplicationFactory
    {
        private readonly string _packageRoot;
        public string PackageRoot => _packageRoot;

        public WorkspaceFactory()
        {
            _packageRoot = Path.Combine(PersistenceRoot, "packages");
            CopyPackages(_packageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Mods:PackageRoots:0", _packageRoot);
            builder.UseSetting("Content:StartupSettings:0", "uno-clash");
            builder.UseSetting("Content:StartupSettings:1", "circular-table");
            builder.UseSetting("Authoring:WritablePackages:uno-clash", "heroscript.uno-clash");
            builder.UseSetting("Authoring:WritablePackages:circular-table", "heroscript.circular-table");
        }

        internal static void CopyPackages(string destination, string? packageRoot = null)
        {
            packageRoot ??= Path.Combine(FindProjectRoot() ??
                throw new InvalidOperationException("HeroScript checkout not found."), "data", "configs");
            foreach (var package in new[] { "default", "uno-clash", "circular-table" })
            {
                var source = Path.Combine(packageRoot, package);
                foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(destination, package, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
        }
    }
}
