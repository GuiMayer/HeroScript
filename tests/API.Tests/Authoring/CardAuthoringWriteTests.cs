using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Authoring;

public sealed class CardAuthoringWriteTests
{
    [Theory]
    [InlineData("uno-clash", "uno_red_0", "uno_red_1", "uno.json", "circular-table")]
    [InlineData("circular-table", "t_red_0", "t_red_1", "table.json", "uno-clash")]
    public async Task AppliesCardsAndKeepsPriorRevisionAndOtherSetting(
        string setting, string cardId, string neighbor, string catalog, string otherSetting)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var originalRevision = await Revision(client, setting);
        var otherBefore = await Revision(client, otherSetting);
        var path = Path.Combine(factory.PackageRoot, setting, "Resources", "cards", catalog);
        var before = await File.ReadAllTextAsync(path);
        using var originalSource = JsonDocument.Parse(before);
        var untouched = originalSource.RootElement.GetProperty(neighbor).GetRawText();

        var (etag, card) = await Read(client, setting, cardId);
        var updated = JsonNode.Parse(card.Source.GetRawText())!.AsObject();
        updated["tags"]!.AsArray().Add("authoring_stage_three");
        var clone = updated.DeepClone().AsObject();
        clone["cardId"] = "stage_three_new";
        var request = new CardAuthoringChangeRequest
        {
            ChangeId = Guid.NewGuid(),
            Operations =
            [
                Operation(CardAuthoringOperationType.Update, cardId, updated),
                Operation(CardAuthoringOperationType.Create, "stage_three_new", clone)
            ]
        };

        var invalidCard = updated.DeepClone().AsObject();
        invalidCard["components"] = new JsonArray();
        using var invalid = await Send(client, setting, etag, request with
        {
            Operations = [Operation(CardAuthoringOperationType.Update, cardId, invalidCard)]
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(path));
        Assert.Equal(originalRevision, await Revision(client, setting));

        using var applied = await Send(client, setting, etag, request);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var receipt = (await applied.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!;
        Assert.Equal("applied", receipt.State);
        Assert.Equal(etag, receipt.PreviousSourceVersion);
        Assert.NotEqual(etag, receipt.SourceVersion);
        Assert.NotEqual(originalRevision, receipt.ActiveRevisions[setting]);
        Assert.Equal(receipt.ActiveRevisions[setting], await Revision(client, setting));
        Assert.Equal(otherBefore, await Revision(client, otherSetting));

        using var written = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(untouched, written.RootElement.GetProperty(neighbor).GetRawText());
        Assert.True(written.RootElement.TryGetProperty("stage_three_new", out _));
        var (writtenVersion, edited) = await Read(client, setting, cardId);
        Assert.Equal(receipt.SourceVersion, writtenVersion);
        Assert.Contains(edited.Effective.GetProperty("tags").EnumerateArray(), value => value.GetString() == "authoring_stage_three");

        var old = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/content/cards/{cardId}?configName={setting}&revision={originalRevision}");
        Assert.DoesNotContain(old.GetProperty("definition").GetProperty("tags").EnumerateArray(),
            value => value.GetString() == "authoring_stage_three");
        var persisted = await client.GetFromJsonAsync<CardAuthoringChangeReceipt>(root + $"/changes/{request.ChangeId}");
        Assert.Equal(receipt.SourceVersion, persisted!.SourceVersion);

        using var retry = await Send(client, setting, etag, request);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(receipt.SourceVersion, (await retry.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!.SourceVersion);
        using var conflict = await Send(client, setting, etag, request with
        {
            Operations = [Operation(CardAuthoringOperationType.Delete, "stage_three_new")]
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var stale = await Send(client, setting, etag, request with { ChangeId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var delete = new CardAuthoringChangeRequest
        {
            ChangeId = Guid.NewGuid(),
            Operations = [Operation(CardAuthoringOperationType.Delete, "stage_three_new")]
        };
        using var deleted = await Send(client, setting, writtenVersion, delete);
        deleted.EnsureSuccessStatusCode();
        using var missing = await client.GetAsync(root + "/definitions/cards/stage_three_new");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var final = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(untouched, final.RootElement.GetProperty(neighbor).GetRawText());
    }

    [Theory]
    [InlineData(AuthoringWriteCheckpoint.FileReplaced, "aborted")]
    [InlineData(AuthoringWriteCheckpoint.SourcesConfirmed, "applied")]
    public async Task RecoversInterruptedMultiFileChangeOnRestart(AuthoringWriteCheckpoint stop, string expectedState)
    {
        using var handle = new WriteFactory(stop: stop, includeEmptyPackage: true);
        var root = handle.PersistenceRoot;
        string previousRevision;
        var manifest = Path.Combine(handle.PackageRoot, "empty-cards", "package.json");
        var manifestBefore = await File.ReadAllBytesAsync(manifest);
        var catalog = Path.Combine(handle.PackageRoot, "empty-cards", "Resources", "cards", "authoring.json");
        var change = CreateInEmptySetting();
        string version;
        using (var client = Open(handle))
        {
            previousRevision = await Revision(client, "empty-cards");
            using var before = await client.GetAsync(Setting("empty-cards") + "/definitions/cards");
            before.EnsureSuccessStatusCode();
            version = before.Headers.ETag!.Tag;
            using var failed = await Send(client, "empty-cards", version, change);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.True(File.Exists(catalog));
            if (stop == AuthoringWriteCheckpoint.FileReplaced)
                Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(manifest));
            var receipt = await client.GetFromJsonAsync<CardAuthoringChangeReceipt>(
                Setting("empty-cards") + $"/changes/{change.ChangeId}");
            Assert.Equal("recovering", receipt!.State);
            Assert.Equal(previousRevision, await Revision(client, "empty-cards"));
        }
        await handle.DisposeAsync();

        using var restarted = new WriteFactory(root, includeEmptyPackage: true);
        using var next = Open(restarted);
        var completed = await next.GetFromJsonAsync<CardAuthoringChangeReceipt>(
            Setting("empty-cards") + $"/changes/{change.ChangeId}");
        Assert.Equal(expectedState, completed!.State);
        using var retry = await Send(next, "empty-cards", version, change);
        Assert.Equal(expectedState == "applied" ? HttpStatusCode.OK : HttpStatusCode.Conflict, retry.StatusCode);
        if (expectedState == "applied")
        {
            Assert.True(File.Exists(catalog));
            Assert.Equal(completed.ActiveRevisions["empty-cards"], await Revision(next, "empty-cards"));
            Assert.NotEqual(previousRevision, completed.ActiveRevisions["empty-cards"]);
        }
        else
        {
            Assert.False(File.Exists(catalog));
            Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(manifest));
            Assert.Equal(previousRevision, await Revision(next, "empty-cards"));
        }
    }

    [Fact]
    public async Task CompetingChangesCannotBothWriteFromTheSameSourceVersion()
    {
        using var factory = new WriteFactory();
        using var first = Open(factory);
        using var second = Open(factory);
        var (etag, detail) = await Read(first, "uno-clash", "uno_red_0");
        CardAuthoringChangeRequest Create(string tag)
        {
            var source = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
            source["tags"]!.AsArray().Add(tag);
            return new()
            {
                ChangeId = Guid.NewGuid(),
                Operations = [Operation(CardAuthoringOperationType.Update, "uno_red_0", source)]
            };
        }
        var results = await Task.WhenAll(Send(first, "uno-clash", etag, Create("version_a")),
            Send(second, "uno-clash", etag, Create("version_b")));
        using var left = results[0];
        using var right = results[1];
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task RecoveryPreservesAFileChangedExternallyAfterAnInterruption()
    {
        using var factory = new WriteFactory(stop: AuthoringWriteCheckpoint.FileReplaced, includeEmptyPackage: true);
        var catalog = Path.Combine(factory.PackageRoot, "empty-cards", "Resources", "cards", "authoring.json");
        using (var client = Open(factory))
        {
            using var before = await client.GetAsync(Setting("empty-cards") + "/definitions/cards");
            using var failed = await Send(client, "empty-cards", before.Headers.ETag!.Tag, CreateInEmptySetting());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        }
        await factory.DisposeAsync();
        var external = "{}" + Environment.NewLine;
        await File.WriteAllTextAsync(catalog, external);
        using var disabled = new WriteFactory(factory.PersistenceRoot, includeEmptyPackage: true, applyEnabled: false);
        Assert.Throws<IOException>(() => Open(disabled));
        using var restarted = new WriteFactory(factory.PersistenceRoot, includeEmptyPackage: true);
        Assert.Throws<IOException>(() => Open(restarted));
        Assert.Equal(external, await File.ReadAllTextAsync(catalog));
    }

    [Fact]
    public async Task HistoricalDraftCannotReplaceManagedSourceRevision()
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        using var draft = await client.PostAsync("/api/v1/admin/settings/uno-clash/drafts", null);
        draft.EnsureSuccessStatusCode();
        var created = await draft.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("draftId").GetGuid();
        using var publish = await client.PostAsJsonAsync($"/api/v1/admin/content/drafts/{id}/publish",
            new { expectedVersion = created.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash", "uno_red_0", "uno.json")]
    [InlineData("circular-table", "t_red_0", "table.json")]
    public async Task CompensationPreviewsAndRestoresMixedCardChangesAcrossRestart(
        string setting, string cardId, string catalog)
    {
        using var factory = new WriteFactory();
        var path = Path.Combine(factory.PackageRoot, setting, "Resources", "cards", catalog);
        CardAuthoringChangeReceipt prior;
        CardAuthoringChangeReceipt applied;
        byte[] before;
        byte[] after;
        using (var client = Open(factory))
        {
            var (etag, detail) = await Read(client, setting, cardId);
            var template = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
            var oldCard = template.DeepClone().AsObject();
            oldCard["cardId"] = "stage_four_old";
            prior = await Apply(client, setting, etag, Change(
                Operation(CardAuthoringOperationType.Create, "stage_four_old", oldCard)));
            before = await File.ReadAllBytesAsync(path);

            var added = template.DeepClone().AsObject();
            added["cardId"] = "stage_four_new";
            template["tags"]!.AsArray().Add("stage_four_edit");
            applied = await Apply(client, setting, prior.SourceVersion, Change(
                Operation(CardAuthoringOperationType.Update, cardId, template),
                Operation(CardAuthoringOperationType.Delete, "stage_four_old"),
                Operation(CardAuthoringOperationType.Create, "stage_four_new", added)));
            after = await File.ReadAllBytesAsync(path);
            Assert.True(applied.CanUndo);
        }
        await factory.DisposeAsync();

        using var restarted = new WriteFactory(factory.PersistenceRoot);
        using var next = Open(restarted);
        var undo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = applied.ChangeId };
        using var preview = await Send(next, setting, applied.SourceVersion, undo, "/changes/validate");
        preview.EnsureSuccessStatusCode();
        var previewResult = (await preview.Content.ReadFromJsonAsync<CardAuthoringValidation>())!;
        Assert.Equal(prior.ActiveRevisions[setting], previewResult.CandidateRevisions[setting]);
        Assert.Equal(after, await File.ReadAllBytesAsync(path));
        Assert.Equal(applied.ActiveRevisions[setting], await Revision(next, setting));

        var undone = await Apply(next, setting, applied.SourceVersion, undo, $"/changes/{applied.ChangeId}/undo");
        Assert.Equal(applied.ChangeId, undone.UndoOf);
        Assert.True(undone.CanRedo);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(prior.ActiveRevisions[setting], await Revision(next, setting));
        var original = await next.GetFromJsonAsync<CardAuthoringChangeReceipt>(Setting(setting) + $"/changes/{applied.ChangeId}");
        Assert.Equal(undo.ChangeId, original!.CompensatedBy);
        Assert.False(original.CanUndo);

        var retried = await Apply(next, setting, applied.SourceVersion, undo);
        Assert.Equal(undone.ChangeId, retried.ChangeId);
        using var duplicate = await Send(next, setting, undone.SourceVersion, undo with { ChangeId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var wrongTarget = await Send(next, setting, undone.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), RedoOf = applied.ChangeId });
        Assert.Equal(HttpStatusCode.Conflict, wrongTarget.StatusCode);

        var redo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId };
        var redone = await Apply(next, setting, undone.SourceVersion, redo, $"/changes/{undone.ChangeId}/redo");
        Assert.Equal(undone.ChangeId, redone.RedoOf);
        Assert.True(redone.CanUndo);
        Assert.Equal(after, await File.ReadAllBytesAsync(path));
        Assert.Equal(applied.ActiveRevisions[setting], await Revision(next, setting));

        var again = await Apply(next, setting, redone.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = redone.ChangeId });
        Assert.Equal(redone.ChangeId, again.UndoOf);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task UndoKeepsIndependentCardsInSameFileAndRejectsAnEditToTheAffectedCard()
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        const string setting = "uno-clash";
        var (source, first) = await Read(client, setting, "uno_red_0");
        var changed = await Apply(client, setting, source, Change(WithTag(first, "target_tag")));
        var (_, second) = await Read(client, setting, "uno_red_1");
        var independent = await Apply(client, setting, changed.SourceVersion, Change(WithTag(second, "keep_tag")));
        var undo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = changed.ChangeId };

        using var stale = await Send(client, setting, changed.SourceVersion, undo);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var crossSetting = await Send(client, "circular-table", independent.SourceVersion, undo);
        Assert.Equal(HttpStatusCode.NotFound, crossSetting.StatusCode);
        var undone = await Apply(client, setting, independent.SourceVersion, undo);
        var (_, firstAfter) = await Read(client, setting, first.Card.DefinitionId);
        var (_, secondAfter) = await Read(client, setting, second.Card.DefinitionId);
        Assert.False(HasTag(firstAfter, "target_tag"));
        Assert.True(HasTag(secondAfter, "keep_tag"));

        var followup = await Apply(client, setting, undone.SourceVersion, Change(WithTag(firstAfter, "later_edit")));
        var redo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId };
        using var conflict = await Send(client, setting, followup.SourceVersion, redo);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var (_, untouched) = await Read(client, setting, first.Card.DefinitionId);
        Assert.True(HasTag(untouched, "later_edit"));
        Assert.False(HasTag(untouched, "target_tag"));
    }

    [Fact]
    public async Task UndoRevalidatesNewReferencesBeforeRemovingACard()
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        const string setting = "uno-clash";
        var (etag, detail) = await Read(client, setting, "uno_red_0");
        var clone = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
        clone["cardId"] = "later_referenced";
        var applied = await Apply(client, setting, etag, Change(
            Operation(CardAuthoringOperationType.Create, "later_referenced", clone)));

        var pool = Path.Combine(factory.PackageRoot, setting, "Resources", "card-pools", "uno.json");
        var source = JsonNode.Parse(await File.ReadAllTextAsync(pool))!.AsObject();
        source["uno_deck_pool"]!["explicitCardIds"]!.AsArray().Add("later_referenced");
        await File.WriteAllTextAsync(pool, source.ToJsonString());
        var (fresh, _) = await Read(client, setting, "later_referenced");
        var undo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = applied.ChangeId };
        using var rejected = await Send(client, setting, fresh, undo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("validation").GetProperty("diagnostics").EnumerateArray(), entry =>
            entry.GetProperty("definitionId").GetString() == "later_referenced" &&
            entry.GetProperty("code").GetString() == "CARD_GRAPH_INVALID");
        Assert.Equal(applied.ActiveRevisions[setting], await Revision(client, setting));
        Assert.Equal(fresh, (await Read(client, setting, "later_referenced")).ETag);
    }

    [Fact]
    public async Task InterruptedUndoRecoversItsManifestFilesAndHistoryLink()
    {
        using var seed = new WriteFactory(includeEmptyPackage: true);
        const string setting = "empty-cards";
        var route = Setting(setting);
        var manifest = Path.Combine(seed.PackageRoot, setting, "package.json");
        var original = await File.ReadAllBytesAsync(manifest);
        var catalog = Path.Combine(seed.PackageRoot, setting, "Resources", "cards", "authoring.json");
        CardAuthoringChangeReceipt applied;
        using (var client = Open(seed))
        {
            using var get = await client.GetAsync(route + "/definitions/cards");
            applied = await Apply(client, setting, get.Headers.ETag!.Tag, CreateInEmptySetting());
        }
        await seed.DisposeAsync();

        var undo = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = applied.ChangeId };
        using var interrupted = new WriteFactory(seed.PersistenceRoot, AuthoringWriteCheckpoint.SourcesConfirmed, includeEmptyPackage: true);
        using (var client = Open(interrupted))
        {
            using var pending = await Send(client, setting, applied.SourceVersion, undo);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        }
        await interrupted.DisposeAsync();

        using var restored = new WriteFactory(seed.PersistenceRoot, includeEmptyPackage: true);
        using var next = Open(restored);
        Assert.Equal(original, await File.ReadAllBytesAsync(manifest));
        Assert.False(File.Exists(catalog));
        var old = await next.GetFromJsonAsync<CardAuthoringChangeReceipt>(route + $"/changes/{applied.ChangeId}");
        Assert.Equal(undo.ChangeId, old!.CompensatedBy);
        var appliedUndo = await Apply(next, setting, applied.SourceVersion, undo);
        Assert.True(appliedUndo.CanRedo);

        var redo = await Apply(next, setting, appliedUndo.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), RedoOf = undo.ChangeId });
        Assert.True(File.Exists(catalog));
        Assert.Equal(applied.ActiveRevisions[setting], redo.ActiveRevisions[setting]);
    }

    private static CardAuthoringChangeRequest Change(params CardAuthoringOperation[] operations) =>
        new() { ChangeId = Guid.NewGuid(), Operations = operations };

    private static CardAuthoringOperation WithTag(CardAuthoringDefinition card, string tag)
    {
        var source = JsonNode.Parse(card.Source.GetRawText())!.AsObject();
        source["tags"]!.AsArray().Add(tag);
        return Operation(CardAuthoringOperationType.Update, card.Card.DefinitionId, source);
    }

    private static bool HasTag(CardAuthoringDefinition card, string tag) =>
        card.Source.GetProperty("tags").EnumerateArray().Any(item => item.GetString() == tag);

    private static async Task<CardAuthoringChangeReceipt> Apply(HttpClient client, string setting, string etag,
        CardAuthoringChangeRequest request, string route = "/changes")
    {
        using var response = await Send(client, setting, etag, request, route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!;
    }

    private static CardAuthoringChangeRequest CreateInEmptySetting() => new()
    {
        ChangeId = Guid.NewGuid(),
        Operations = [Operation(CardAuthoringOperationType.Create, "first", JsonNode.Parse(
            """{"cardId":"first","components":[{"type":"condition","componentId":"ready","expression":"1"}]}"""))]
    };

    private static CardAuthoringOperation Operation(CardAuthoringOperationType operation, string id, JsonNode? value = null) =>
        new() { Kind = "cards", DefinitionId = id, Operation = operation,
            Definition = value is null ? null : JsonSerializer.SerializeToElement(value) };

    private static string Setting(string id) => $"/api/v1/admin/authoring/settings/{id}";

    private static async Task<string> Revision(HttpClient client, string setting)
    {
        var response = await client.GetFromJsonAsync<JsonElement>($"/api/v1/content/revisions?configName={setting}");
        return response.GetProperty("currentRevision").GetString()!;
    }

    private static async Task<(string ETag, CardAuthoringDefinition Card)> Read(HttpClient client, string setting, string card)
    {
        using var response = await client.GetAsync(Setting(setting) + "/definitions/cards/" + card);
        response.EnsureSuccessStatusCode();
        return (response.Headers.ETag!.Tag, (await response.Content.ReadFromJsonAsync<CardAuthoringDefinition>())!);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string setting, string etag,
        CardAuthoringChangeRequest request, string route = "/changes")
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Setting(setting) + route)
        {
            Content = route.EndsWith("/undo", StringComparison.Ordinal) || route.EndsWith("/redo", StringComparison.Ordinal)
                ? JsonContent.Create(new AuthoringCompensationRequest(request.ChangeId))
                : JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(message);
    }

    private static HttpClient Open(WriteFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "dev-admin-key");
        return client;
    }

    private sealed class WriteFactory : TestWebApplicationFactory
    {
        private readonly AuthoringWriteCheckpoint? _stop;
        private readonly bool _includeEmptyPackage;
        private readonly bool _applyEnabled;

        public WriteFactory(string? root = null, AuthoringWriteCheckpoint? stop = null,
            bool includeEmptyPackage = false, bool applyEnabled = true)
            : base(root ?? Path.Combine(Path.GetTempPath(), "HeroScript", "authoring-write-tests", Guid.NewGuid().ToString("N")))
        {
            _stop = stop;
            _includeEmptyPackage = includeEmptyPackage;
            _applyEnabled = applyEnabled;
            PackageRoot = Path.Combine(PersistenceRoot, "packages");
            if (root is null)
            {
                CardAuthoringWorkspaceTests.WorkspaceFactory.CopyPackages(PackageRoot);
                if (includeEmptyPackage)
                {
                    var package = Path.Combine(PackageRoot, "empty-cards");
                    Directory.CreateDirectory(Path.Combine(package, "settings"));
                    File.WriteAllText(Path.Combine(package, "package.json"),
                        """{"packageId":"fixture.empty","version":"1.0.0","dependencies":[{"packageId":"heroscript.base","versionRange":"1.1.0"}]}""");
                    File.WriteAllText(Path.Combine(package, "settings", "empty-cards.json"),
                        """{"settingId":"empty-cards","packages":[{"packageId":"fixture.empty","versionRange":"1.0.0"}]}""");
                }
            }
        }

        public string PackageRoot { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Mods:PackageRoots:0", PackageRoot);
            builder.UseSetting("Content:StartupSettings:0", "uno-clash");
            builder.UseSetting("Content:StartupSettings:1", "circular-table");
            builder.UseSetting("Authoring:ApplyEnabled", _applyEnabled ? "true" : "false");
            builder.UseSetting("Authoring:WritablePackages:uno-clash", "heroscript.uno-clash");
            builder.UseSetting("Authoring:WritablePackages:circular-table", "heroscript.circular-table");
            if (_includeEmptyPackage)
            {
                builder.UseSetting("Content:StartupSettings:2", "empty-cards");
                builder.UseSetting("Authoring:WritablePackages:empty-cards", "fixture.empty");
            }
            if (_stop is { } stop)
                builder.ConfigureServices(services => services.AddSingleton<Action<AuthoringWriteCheckpoint>>(
                    point =>
                    {
                        if (point == stop)
                            throw new IOException($"Simulated interruption at {point}.");
                    }));
        }
    }
}
