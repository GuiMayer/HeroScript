using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Authoring;

public sealed class CardAuthoringWriteTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("uno-clash", "circular-table", "combat-rules", "standard_combat")]
    [InlineData("circular-table", "uno-clash", "flow-rules", "standard_flow")]
    [InlineData("uno-clash", "circular-table", "status-effects", "burning")]
    [InlineData("circular-table", "uno-clash", "keywords", "fixture_keyword")]
    public async Task CustomizesInheritedDefinitions(string setting, string otherSetting, string kind, string id)
    {
        using var factory = new WriteFactory();
        var basePackage = Path.Combine(factory.PackageRoot, "default");
        var fileName = kind == "status-effects" ? "status_effects" : id;
        var basePath = Path.Combine(basePackage, "Resources", kind, fileName + ".json");
        if (kind == "keywords")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            await File.WriteAllTextAsync(basePath, """{"fixture_keyword":{"name":"Keyword","configuration":{"amount":3}}}""");
            var manifestPath = Path.Combine(basePackage, "package.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
            manifest["content"]!.AsArray().Add(new JsonObject
            {
                ["operation"] = "definition", ["kind"] = kind,
                ["sourcePath"] = $"Resources/{kind}/{fileName}.json", ["artifactPath"] = $"{kind}/{fileName}.json"
            });
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
        }
        using var client = Open(factory);
        var route = Setting(setting) + "/definitions/" + kind;
        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(route + "?q=" + id);
        var item = Assert.Single(page!.Items);
        Assert.True(item.CanCustomize);
        Assert.False(item.CanEdit);
        var originalBytes = await File.ReadAllBytesAsync(basePath);
        var otherRevision = await Revision(client, otherSetting);
        async Task<AuthoringDefinitionDetail> ReadRule() =>
            (await client.GetFromJsonAsync<AuthoringDefinitionDetail>(route + "/" + id))!;
        var original = await ReadRule();
        var desired = JsonNode.Parse(original.Effective.GetRawText())!.AsObject();
        var field = kind switch
        {
            "combat-rules" => "flow",
            "flow-rules" => "allowMapNavigation",
            "status-effects" => "DefaultDuration",
            _ => "configuration"
        };
        switch (kind)
        {
            case "combat-rules":
                desired[field]!["automaticResolution"]!["maxAutomaticSteps"] = 101;
                break;
            case "flow-rules":
                desired[field] = false;
                break;
            case "status-effects":
                desired[field] = 4;
                break;
            default:
                desired[field]!["amount"] = 7;
                break;
        }
        CardAuthoringOperation Customize(JsonObject? value) => new()
        {
            Kind = kind, DefinitionId = id,
            Operation = value is null ? CardAuthoringOperationType.ResetCustomization : CardAuthoringOperationType.Customize,
            Definition = value is null ? null : JsonSerializer.SerializeToElement(value)
        };
        if (AuthoringDefinitionKinds.Identity(kind) is { } identity)
        {
            var invalid = desired.DeepClone().AsObject();
            var identityKey = invalid.Select(property => property.Key).Single(key => key.Equals(identity, StringComparison.OrdinalIgnoreCase));
            invalid[identityKey] = "wrong_id";
            using var bad = await Send(client, setting, page.SourceVersion, Change(Customize(invalid)), "/changes/validate");
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        var change = Change(Customize(desired));
        using var preview = await Send(client, setting, page.SourceVersion, change, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(Path.Combine(factory.PackageRoot, setting, "patches")));
        var applied = await Apply(client, setting, page.SourceVersion, change);
        var patchPath = Path.Combine(factory.PackageRoot, Assert.Single(applied.ChangedFiles,
            path => path.Contains($"/patches/{kind}/", StringComparison.Ordinal)));
        using var patch = JsonDocument.Parse(await File.ReadAllBytesAsync(patchPath));
        Assert.Equal(field, Assert.Single(patch.RootElement.EnumerateObject()).Name);
        var personalized = await ReadRule();
        Assert.True(personalized.Item.HasCustomization);
        Assert.True(JsonElement.DeepEquals(original.Source, personalized.Source));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(desired), personalized.Effective));
        var reset = await Apply(client, setting, applied.SourceVersion, Change(Customize(null)));
        Assert.False(File.Exists(patchPath));
        Assert.True(JsonElement.DeepEquals(original.Effective, (await ReadRule()).Effective));
        var undone = await Apply(client, setting, reset.SourceVersion, new() { ChangeId = Guid.NewGuid(), UndoOf = reset.ChangeId });
        Assert.True(File.Exists(patchPath));
        Assert.True((await ReadRule()).Item.HasCustomization);
        await Apply(client, setting, undone.SourceVersion, new() { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId });
        Assert.False(File.Exists(patchPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(basePath));
        Assert.Equal(otherRevision, await Revision(client, otherSetting));
    }

    [Theory]
    [InlineData("uno-clash", "circular-table", false)]
    [InlineData("circular-table", "uno-clash", true)]
    public async Task CustomizesInheritedCardsWithoutChangingTheirSource(string setting, string other, bool newPackage)
    {
        using var factory = new WriteFactory();
        var packageFolder = Path.Combine(factory.PackageRoot, setting);
        if (newPackage)
        {
            var path = Path.Combine(packageFolder, "settings", setting + ".json");
            var settingJson = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            settingJson["packages"]!.AsArray().Add(new JsonObject { ["packageId"] = "custom.cards", ["versionRange"] = "1.0.0" });
            settingJson["authoringPackageId"] = "custom.cards";
            await File.WriteAllTextAsync(path, settingJson.ToJsonString());
            packageFolder = Directory.CreateDirectory(Path.Combine(factory.PackageRoot, "custom-cards")).FullName;
            await File.WriteAllTextAsync(Path.Combine(packageFolder, "package.json"),
                """{"packageId":"custom.cards","version":"1.0.0"}""");
        }

        using var client = Open(factory);
        const string id = "basic_attack";
        var basePath = Path.Combine(factory.PackageRoot, "default", "Resources", "cards", "card_catalog.json");
        var baseBytes = await File.ReadAllBytesAsync(basePath);
        var (version, original) = await Read(client, setting, id);
        var otherRevision = await Revision(client, other);
        Assert.True(original.Card.CanCustomize);
        Assert.False(original.Card.CanEdit);
        Assert.False(original.Card.HasCustomization);
        var definition = JsonNode.Parse(original.Effective.GetRawText())!.AsObject();
        var tags = definition["tags"]?.AsArray() ?? [];
        if (definition["tags"] is null)
            definition["tags"] = tags;
        tags.Add("custom_patch_tag");
        var change = Change(Operation(CardAuthoringOperationType.Customize, id, definition));

        var invalid = definition.DeepClone().AsObject();
        invalid["tags"] = new JsonArray(JsonValue.Create(""));
        using var invalidResponse = await Send(client, setting, version,
            Change(Operation(CardAuthoringOperationType.Customize, id, invalid)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);
        using var preview = await Send(client, setting, version, change, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(Path.Combine(packageFolder, "patches")));

        var applied = await Apply(client, setting, version, change);
        var patchPath = Path.Combine(factory.PackageRoot, Assert.Single(applied.ChangedFiles,
            path => path.Contains("/patches/cards/", StringComparison.Ordinal)));
        using var patch = JsonDocument.Parse(await File.ReadAllBytesAsync(patchPath));
        Assert.Equal("tags", Assert.Single(patch.RootElement.EnumerateObject()).Name);
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(packageFolder, "package.json")));
        var envelope = Assert.Single(manifest.RootElement.GetProperty("content").EnumerateArray(), entry =>
            entry.GetProperty("operation").GetString()!.Equals("patch", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Core.Determinism.CanonicalJson.ComputeHash(original.Effective), envelope.GetProperty("expectedHash").GetString());
        Assert.Equal(id, envelope.GetProperty("target").GetProperty("definitionId").GetString());
        var (_, customized) = await Read(client, setting, id);
        Assert.True(customized.Card.HasCustomization);
        Assert.True(customized.Card.CanCustomize);
        Assert.True(JsonElement.DeepEquals(original.Source, customized.Source));
        Assert.Contains(customized.Effective.GetProperty("tags").EnumerateArray(), tag => tag.GetString() == "custom_patch_tag");
        using var stale = await Send(client, setting, version, Change(Operation(CardAuthoringOperationType.Customize, id, definition)));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var undone = await Apply(client, setting, applied.SourceVersion, new() { ChangeId = Guid.NewGuid(), UndoOf = applied.ChangeId });
        Assert.False(File.Exists(patchPath));
        Assert.True(JsonElement.DeepEquals(original.Effective, (await Read(client, setting, id)).Card.Effective));
        var redone = await Apply(client, setting, undone.SourceVersion, new() { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId });
        Assert.True(File.Exists(patchPath));

        tags.Add("second_patch_tag");
        var updated = await Apply(client, setting, redone.SourceVersion,
            Change(Operation(CardAuthoringOperationType.Customize, id, definition)));
        Assert.Contains((await Read(client, setting, id)).Card.Effective.GetProperty("tags").EnumerateArray(),
            tag => tag.GetString() == "second_patch_tag");
        var reset = await Apply(client, setting, updated.SourceVersion, Change(Operation(CardAuthoringOperationType.ResetCustomization, id)));
        Assert.False(File.Exists(patchPath));
        Assert.False((await Read(client, setting, id)).Card.Card.HasCustomization);
        var restore = new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = reset.ChangeId };
        var changedBase = JsonNode.Parse(baseBytes)!.AsObject();
        var baseCard = changedBase[id]!.AsObject();
        var baseTags = baseCard["tags"]?.AsArray() ?? [];
        if (baseCard["tags"] is null)
            baseCard["tags"] = baseTags;
        baseTags.Add("external_base_edit");
        await File.WriteAllTextAsync(basePath, changedBase.ToJsonString());
        var (changedVersion, _) = await Read(client, setting, id);
        using var rejected = await Send(client, setting, changedVersion, restore, "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("Patch precondition failed", await rejected.Content.ReadAsStringAsync());
        Assert.False(File.Exists(patchPath));
        await File.WriteAllBytesAsync(basePath, baseBytes);
        await Apply(client, setting, reset.SourceVersion, restore);
        Assert.True(File.Exists(patchPath));
        Assert.Equal(baseBytes, await File.ReadAllBytesAsync(basePath));
        Assert.Equal(otherRevision, await Revision(client, other));
    }

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

    [Fact]
    public async Task AuthoredRevisionsRebuildFromSourcesAndExistingRunsKeepTheirRevision()
    {
        using var original = new WriteFactory(performanceDiagnostics: true);
        var settings = new[]
        {
            (Id: "uno-clash", Card: "uno_red_0", Catalog: "uno.json", Run: "uno_clash_run", Mode: "uno_classic", Player: "player"),
            (Id: "circular-table", Card: "t_red_0", Catalog: "table.json", Run: "table_run", Mode: "table_mode", Player: "seat_0")
        };
        var history = new Dictionary<string, (Guid OldRun, string Original, Guid NewRun, string Authored)>();
        const string tag = "stage_five_rebuild";
        using (var client = Open(original))
        {
            foreach (var setting in settings)
            {
                var catalog = Path.Combine(original.PackageRoot, setting.Id, "Resources", "cards", setting.Catalog);
                using var sourceBefore = JsonDocument.Parse(await File.ReadAllBytesAsync(catalog));
                var initialRevision = await Revision(client, setting.Id);
                var oldRun = await StartRun(client, setting.Id, setting.Run, setting.Mode, setting.Player,
                    initialRevision, 1739);
                var (version, card) = await Read(client, setting.Id, setting.Card);
                var change = Change(WithTag(card, tag));
                using var preview = await Send(client, setting.Id, version, change, "/changes/validate");
                preview.EnsureSuccessStatusCode();
                var previewTiming = string.Join(", ", preview.Headers.GetValues("Server-Timing"));
                Assert.Contains("authoring_validation;dur=", previewTiming);
                using var appliedResponse = await Send(client, setting.Id, version, change);
                appliedResponse.EnsureSuccessStatusCode();
                var applyTiming = string.Join(", ", appliedResponse.Headers.GetValues("Server-Timing"));
                foreach (var phase in new[] { "authoring_validation", "authoring_prepare", "authoring_apply" })
                    Assert.Contains(phase + ";dur=", applyTiming);
                output.WriteLine($"{setting.Id} preview: {previewTiming}");
                output.WriteLine($"{setting.Id} apply: {applyTiming}");
                var applied = (await appliedResponse.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!;
                using var sourceAfter = JsonDocument.Parse(await File.ReadAllBytesAsync(catalog));
                var appliedCard = sourceAfter.RootElement.GetProperty(setting.Card);
                Assert.DoesNotContain("\r\r\n", appliedCard.GetProperty("tags").GetRawText());
                foreach (var property in sourceBefore.RootElement.GetProperty(setting.Card).EnumerateObject())
                    if (!property.NameEquals("tags"))
                        Assert.Equal(property.Value.GetRawText(), appliedCard.GetProperty(property.Name).GetRawText());
                var authored = applied.ActiveRevisions[setting.Id];
                Assert.NotEqual(initialRevision, authored);
                Assert.Equal(authored, await Revision(client, setting.Id));
                await AssertRunRevision(client, oldRun, initialRevision);
                var newRun = await StartRun(client, setting.Id, setting.Run, setting.Mode, setting.Player,
                    authored, 1740);
                history.Add(setting.Id, (oldRun, initialRevision, newRun, authored));
            }
        }
        await original.DisposeAsync();

        using var restarted = new WriteFactory(original.PersistenceRoot);
        using (var client = Open(restarted))
        {
            foreach (var setting in settings)
            {
                var expected = history[setting.Id];
                Assert.Equal(expected.Authored, await Revision(client, setting.Id));
                await AssertRunRevision(client, expected.OldRun, expected.Original);
                await AssertRunRevision(client, expected.NewRun, expected.Authored);
                var historical = await ReadPublishedCard(client, setting.Id, setting.Card, expected.Original);
                Assert.DoesNotContain(historical.GetProperty("tags").EnumerateArray(), item => item.GetString() == tag);
            }
        }

        using var clean = new WriteFactory(applyEnabled: false, packageSource: original.PackageRoot);
        Assert.False(Directory.Exists(Path.Combine(clean.PersistenceRoot, "content")));
        Assert.False(Directory.Exists(Path.Combine(clean.PackageRoot, ".heroscript-authoring")));
        using var cleanClient = Open(clean);
        foreach (var setting in settings)
        {
            var expected = history[setting.Id];
            Assert.Equal(expected.Authored, await Revision(cleanClient, setting.Id));
            var rebuilt = await ReadPublishedCard(cleanClient, setting.Id, setting.Card, expected.Authored);
            Assert.Contains(rebuilt.GetProperty("tags").EnumerateArray(), item => item.GetString() == tag);
            using var absent = await cleanClient.GetAsync($"/api/v1/runs/{expected.OldRun}");
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        }
    }

    [Theory]
    [InlineData("uno-clash", "uno_combat", "uno.json")]
    [InlineData("circular-table", "table_combat", "table.json")]
    public async Task RuleAuthoringListsValidatesWritesAndUndoesRuleDefinitions(string setting, string id, string catalog)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var kinds = await client.GetFromJsonAsync<AuthoringKind[]>(root + "/kinds");
        Assert.NotNull(kinds);
        Assert.Equal(47, kinds.Count(kind => kind.SupportsSchema && kind.CanCreate));
        foreach (var kind in kinds.Where(kind => kind.SupportsSchema))
        {
            var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + $"/kinds/{kind.Kind}/schema");
            Assert.NotNull(schema);
            if (kind.Kind is "powers" or "keywords" or "enemies" or "companions" or "races" or "decks" or "run-events" or "boards" or "zones")
            {
                Assert.Null(schema.IdentityProperty);
                Assert.Equal("object", schema.Schema["type"]!.GetValue<string>());
            }
            else if (kind.Kind == "formulas")
            {
                Assert.Null(schema.IdentityProperty);
                Assert.NotNull(schema.Schema["properties"]?["operations"]);
            }
            else
                Assert.Contains(schema.IdentityProperty, schema.Schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        }

        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/combat-rules?q=" + id);
        Assert.Equal(id, Assert.Single(page!.Items).DefinitionId);
        var references = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/phase-sequences");
        Assert.NotEmpty(references!.Items);
        var inherited = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/flow-rules/standard_flow");
        Assert.False(inherited!.Item.CanEdit);

        var detail = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/combat-rules/" + id);
        Assert.True(detail!.Item.CanEdit);
        Assert.NotEmpty(detail.Provenance);
        var originalRevision = await Revision(client, setting);
        var source = Path.Combine(factory.PackageRoot, setting, "Resources", "combat-rules", catalog);
        var original = await File.ReadAllBytesAsync(source);
        var definition = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
        var invalid = definition.DeepClone().AsObject();
        invalid["defaultPhaseSequenceId"] = "missing_phase_sequence";
        CardAuthoringOperation Rule(JsonObject value) => new()
        {
            Operation = CardAuthoringOperationType.Update, Kind = "combat-rules", DefinitionId = id,
            Definition = JsonSerializer.SerializeToElement(value)
        };
        using var invalidResult = await Send(client, setting, page.SourceVersion, Change(Rule(invalid)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResult.StatusCode);
        Assert.Contains("missing_phase_sequence", await invalidResult.Content.ReadAsStringAsync());

        var resolution = definition["flow"]!["automaticResolution"]!;
        resolution["maxAutomaticSteps"] = resolution["maxAutomaticSteps"]!.GetValue<int>() + 1;
        var change = Change(Rule(definition), new()
        {
            Operation = CardAuthoringOperationType.Create, Kind = "flow-rules", DefinitionId = "authoring_rule_test",
            Definition = JsonSerializer.SerializeToElement(new { flowRulesId = "authoring_rule_test", allowMapNavigation = true })
        });
        using var preview = await Send(client, setting, page.SourceVersion, change, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal(originalRevision, await Revision(client, setting));

        var result = await Apply(client, setting, page.SourceVersion, change);
        var observed = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/combat-rules/" + id);
        Assert.Equal(resolution["maxAutomaticSteps"]!.GetValue<int>(), observed!.Source.GetProperty("flow")
            .GetProperty("automaticResolution").GetProperty("maxAutomaticSteps").GetInt32());
        Assert.Equal(result.ActiveRevisions[setting], await Revision(client, setting));
        Assert.NotEqual(originalRevision, result.ActiveRevisions[setting]);
        using var created = await client.GetAsync(root + "/definitions/flow-rules/authoring_rule_test");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var undone = await Apply(client, setting, result.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = result.ChangeId });
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal(originalRevision, undone.ActiveRevisions[setting]);
        using var absent = await client.GetAsync(root + "/definitions/flow-rules/authoring_rule_test");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash", "uno_setup_table", "Distribuir cartas reais", "uno.json")]
    [InlineData("circular-table", "t_draw", "Draw one", "table.json")]
    public async Task EffectAuthoringValidatesAppliesAndCompensatesAcrossCatalogs(
        string setting, string id, string name, string catalog)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(
            $"{root}/definitions/actions?q={Uri.EscapeDataString(name)}");
        Assert.Contains(page!.Items, item => item.DefinitionId == id && item.CanEdit && item.DisplayName == name);
        var endpoint = $"{root}/definitions/actions/{id}";
        var detail = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(endpoint);
        Assert.NotEmpty(detail!.Provenance);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/actions/schema");
        Assert.Equal("resources", schema!.References["/effects/*/targetResource"]);
        var references = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/resources");
        Assert.NotEmpty(references!.Items);

        CardAuthoringOperation Operation(string kind, string key, JsonObject value,
            CardAuthoringOperationType operation = CardAuthoringOperationType.Create) => new()
        {
            Operation = operation, Kind = kind, DefinitionId = key, Definition = JsonSerializer.SerializeToElement(value)
        };

        var action = JsonNode.Parse(detail.Source.GetRawText())!.AsObject();
        var invalid = action.DeepClone().AsObject();
        invalid["costs"] = JsonNode.Parse("""{"costs":[{"resourceId":"missing_effect_test_resource","amount":1}]}""");
        using var rejected = await Send(client, setting, page.SourceVersion,
            Change(Operation("actions", id, invalid, CardAuthoringOperationType.Update)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("missing_effect_test_resource", await rejected.Content.ReadAsStringAsync());

        var path = Path.Combine(factory.PackageRoot, setting, "Resources", "actions", catalog);
        var original = await File.ReadAllBytesAsync(path);
        var originalRevision = await Revision(client, setting);
        action["displayName"] = "Edited action";
        var change = Change(Operation("actions", id, action, CardAuthoringOperationType.Update),
            Operation("status-effects", "authoring_status", new() { ["StatusId"] = "authoring_status", ["DisplayName"] = "Test" }),
            Operation("powers", "authoring_power", new() { ["name"] = "Power" }),
            Operation("keywords", "authoring_keyword", new() { ["name"] = "Keyword" }));
        using var preview = await Send(client, setting, page.SourceVersion, change, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(originalRevision, await Revision(client, setting));

        var applied = await Apply(client, setting, page.SourceVersion, change);
        var saved = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(endpoint);
        Assert.Equal("Edited action", saved!.Source.GetProperty("displayName").GetString());
        foreach (var (kind, definitionId) in new[] { ("status-effects", "authoring_status"), ("powers", "authoring_power"), ("keywords", "authoring_keyword") })
        {
            var created = await client.GetFromJsonAsync<AuthoringDefinitionDetail>($"{root}/definitions/{kind}/{definitionId}");
            Assert.Equal(definitionId, created!.Item.DefinitionId);
        }
        Assert.Equal(applied.ActiveRevisions[setting], await Revision(client, setting));
        Assert.NotEqual(originalRevision, applied.ActiveRevisions[setting]);

        var undone = await Apply(client, setting, applied.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), UndoOf = applied.ChangeId });
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(originalRevision, undone.ActiveRevisions[setting]);
        using var removed = await client.GetAsync(root + "/definitions/keywords/authoring_keyword");
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        var redone = await Apply(client, setting, undone.SourceVersion,
            new CardAuthoringChangeRequest { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId });
        Assert.Equal(applied.ActiveRevisions[setting], redone.ActiveRevisions[setting]);
        var catalogEdits = Change(
            Operation("powers", "authoring_power", JsonNode.Parse(
                """{"name":"Edited power","settings":{"enabled":false,"steps":[1,"hit",null]}}""")!.AsObject(),
                CardAuthoringOperationType.Update),
            new CardAuthoringOperation
            {
                Operation = CardAuthoringOperationType.Delete, Kind = "keywords", DefinitionId = "authoring_keyword"
            });
        using var editedPreview = await Send(client, setting, redone.SourceVersion, catalogEdits, "/changes/validate");
        Assert.True(editedPreview.IsSuccessStatusCode, await editedPreview.Content.ReadAsStringAsync());
        await Apply(client, setting, redone.SourceVersion, catalogEdits);
        var power = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/powers/authoring_power");
        var settings = power!.Source.GetProperty("settings");
        Assert.False(settings.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, settings.GetProperty("steps").GetArrayLength());
        using var deletedKeyword = await client.GetAsync(root + "/definitions/keywords/authoring_keyword");
        Assert.Equal(HttpStatusCode.NotFound, deletedKeyword.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash", "uno_draw_one")]
    [InlineData("circular-table", "t_no_debt")]
    public async Task ResourceAuthoringValidatesWritesAndDeletes(string setting, string existingFormula)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var formulas = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/formulas");
        var existing = await client.GetFromJsonAsync<AuthoringDefinitionDetail>($"{root}/definitions/formulas/{existingFormula}");
        Assert.True(existing!.Item.CanEdit);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/formulas/schema");
        Assert.Null(schema!.IdentityProperty);
        Assert.NotNull(schema.Schema["properties"]?["parameterConstraints"]);
        Assert.NotNull(schema.Schema["properties"]?["operations"]);

        static CardAuthoringOperation Op(string kind, string? source, CardAuthoringOperationType operation) => new()
        {
            Kind = kind, DefinitionId = "editor_test", Operation = operation,
            Definition = source is null ? null : JsonSerializer.Deserialize<JsonElement>(source)
        };
        static CardAuthoringOperation Create(string kind, string source) => Op(kind, source, CardAuthoringOperationType.Create);

        var create = Change(
            Create("resources", """{"resourceId":"editor_test","displayName":"Test resource","defaultMax":10}"""),
            Create("actor-resource-lifecycle-policies", """
                {"actorResourceLifecyclePolicyId":"editor_test","rules":[{"resourceId":"editor_test","entry":"PreserveCurrent",
                "victory":"PreserveCurrent","defeat":"PreserveCurrent","draw":"PreserveCurrent","abandoned":"PreserveCurrent","retry":"PreserveCurrent"}]}
                """),
            Create("calculation-pipelines", """{"pipelineId":"editor_test","channel":"editor_test","buckets":[{"bucketId":"base","operation":"Add"}]}"""),
            Create("formulas", """{"params":{"base":2},"operations":[{"op":"SET","value":"params.base"}]}"""));
        using var preview = await Send(client, setting, formulas!.SourceVersion, create, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var applied = await Apply(client, setting, formulas.SourceVersion, create);

        var invalid = Change(Op("formulas", """{"operations":[{"op":"UNKNOWN"}]}""", CardAuthoringOperationType.Update));
        using var rejected = await Send(client, setting, applied.SourceVersion, invalid, "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("unsupported operation", await rejected.Content.ReadAsStringAsync());
        using var used = await Send(client, setting, applied.SourceVersion,
            Change(Op("resources", null, CardAuthoringOperationType.Delete)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, used.StatusCode);

        var edited = await Apply(client, setting, applied.SourceVersion,
            Change(Op("formulas", """{"params":{"base":3},"operations":[{"op":"SET","value":"params.base"}]}""", CardAuthoringOperationType.Update)));
        var formula = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/formulas/editor_test");
        Assert.Equal(3, formula!.Source.GetProperty("params").GetProperty("base").GetInt32());

        var kinds = create.Operations.Select(operation => operation!.Kind).ToArray();
        await Apply(client, setting, edited.SourceVersion, Change(kinds.Select(kind => Op(kind, null, CardAuthoringOperationType.Delete)).ToArray()));
        foreach (var kind in kinds)
        {
            using var missing = await client.GetAsync($"{root}/definitions/{kind}/editor_test");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Theory]
    [InlineData("uno-clash", "uno_choose_red")]
    [InlineData("circular-table", "g_t_choose_red")]
    public async Task EntityAuthoringValidatesWritesAndDeletes(string setting, string gambit)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/entities");
        Assert.NotEmpty(page!.Items);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/entities/schema");
        Assert.Equal("resources", schema!.References["/components/*/pools/@key"]);
        var items = schema.Schema["properties"]!["components"]!["items"]!.AsObject();
        var variants = (items["anyOf"] ?? items["oneOf"])!.AsArray();
        Assert.Contains(variants, branch => (string?)branch?["properties"]?["type"]?["const"] == "resources");

        static CardAuthoringOperation Op(string kind, string? source, CardAuthoringOperationType operation) => new()
        {
            Kind = kind, DefinitionId = "editor_entity", Operation = operation,
            Definition = source is null ? null : JsonSerializer.Deserialize<JsonElement>(source)
        };
        static CardAuthoringOperation Create(string kind, string source) => Op(kind, source, CardAuthoringOperationType.Create);
        const string entity = """
            {"definitionId":"editor_entity","displayName":"Test entity",
            "components":[{"type":"resources","componentId":"resources","pools":{"block":{"current":1,"max":10}}}]}
            """;
        var create = Change(Create("entities", entity),
            Create("enemy-pools", """{"enemyPoolId":"editor_entity","entityDefinitionIds":["editor_entity"]}"""),
            Create("support-routines", $$"""{"routineId":"editor_entity","gambitIds":["{{gambit}}"]}"""),
            Create("supports", """
                {"supportId":"editor_entity","countsForOutcome":false,"ownerActivationInterval":1,
                "entityDefinitionId":"editor_entity","routineIds":["editor_entity"],"defaultRoutineId":"editor_entity"}
                """),
            Create("enemies", """{"name":"Enemy"}"""), Create("companions", """{"name":"Companion"}"""),
            Create("races", """{"name":"Race"}"""));
        using var preview = await Send(client, setting, page.SourceVersion, create, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var applied = await Apply(client, setting, page.SourceVersion, create);

        using var invalid = await Send(client, setting, applied.SourceVersion,
            Change(Op("entities", """{"definitionId":"editor_entity","components":[{}]}""", CardAuthoringOperationType.Update)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        using var referenced = await Send(client, setting, applied.SourceVersion,
            Change(Op("entities", null, CardAuthoringOperationType.Delete)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, referenced.StatusCode);

        var updated = await Apply(client, setting, applied.SourceVersion, Change(Op("entities",
            entity.Replace("Test entity", "Updated entity", StringComparison.Ordinal), CardAuthoringOperationType.Update)));
        var saved = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/entities/editor_entity");
        Assert.Equal("Updated entity", saved!.Source.GetProperty("displayName").GetString());

        var kinds = create.Operations.Select(operation => operation!.Kind).ToArray();
        await Apply(client, setting, updated.SourceVersion, Change(kinds.Select(kind => Op(kind, null, CardAuthoringOperationType.Delete)).ToArray()));
        foreach (var kind in kinds)
        {
            using var missing = await client.GetAsync($"{root}/definitions/{kind}/editor_entity");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Theory]
    [InlineData("uno-clash", "uno_red_0", "uno_bot", "uno_choose_red")]
    [InlineData("circular-table", "t_red_0", "table_actor", "g_t_choose_red")]
    public async Task DeckAuthoringValidatesWritesAndDeletes(string setting, string cardId, string entityId, string gambitId)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/card-pools");
        Assert.NotEmpty(page!.Items);
        var rarity = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/card-rarities?q=Legendary");
        Assert.Equal("Legendary", Assert.Single(rarity!.Items).Id);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/deck-modules/schema");
        Assert.Equal("local:cardIds", schema!.References["/startingCardIds/*"]);
        Assert.Equal("/startingCardIds", (string?)schema.Schema["x-authoring-repeatable-references"]?[0]);

        static CardAuthoringOperation Op(string kind, string? source, CardAuthoringOperationType operation) => new()
        {
            Kind = kind, DefinitionId = "editor_deck", Operation = operation,
            Definition = source is null ? null : JsonSerializer.Deserialize<JsonElement>(source)
        };
        static CardAuthoringOperation Create(string kind, string source) => Op(kind, source, CardAuthoringOperationType.Create);
        var create = Change(
            Create("decks", """{"name":"Test Deck"}"""),
            Create("card-pools", $$"""{"poolId":"editor_deck","explicitCardIds":["{{cardId}}"],"rarityWeights":{"Common":4} }"""),
            Create("deck-modules", $$"""{"moduleId":"editor_deck","cardIds":["{{cardId}}"],"startingCardIds":["{{cardId}}","{{cardId}}"]}"""),
            Create("deck-composition-policies", """
                {"policyId":"editor_deck","editWindows":["Preparation"],"groups":[{"groupId":"core","minimum":2,"maximum":2,
                "allowDuplicateDefinitions":true,"moduleIds":["editor_deck"],"startingModuleId":"editor_deck"}]}
                """),
            Create("deck-compartment-profiles", """
                {"profileId":"editor_deck","compartments":[{"compartmentId":"extra","capacity":2,
                "grants":[{"grantId":"ember","bundleId":"core.ember.attack"}]}]}
                """),
            Create("deck-compartment-policies", """{"policyId":"editor_deck","preparationOnly":true}"""),
            Create("calculation-pipelines", """{"pipelineId":"editor_deck","channel":"editor_deck","buckets":[{"bucketId":"base"}]}"""),
            Create("effect-resolution-policies", """
                {"policyId":"editor_deck","rules":[{"ruleId":"default","amountPipelineId":"editor_deck"}],
                "attributes":[{"attributeId":"editor_deck","displayName":"Deck test attribute","pipelineId":"editor_deck"}]}
                """),
            Create("inscriptions", """
                {"inscriptionId":"editor_deck","attributeBonuses":[{"attributeId":"editor_deck",
                "influence":{"influenceId":"bonus","channel":"editor_deck","bucket":"base","value":2}}]}
                """),
            Create("loadout-policies", """
                {"loadoutPolicyId":"editor_deck","capacity":2,"inventoryScope":"Actor","capacityOnly":true,
                "allowDuplicateDefinitions":true,"inscriptionIds":["editor_deck"],"equipmentWindows":["Preparation"]}
                """),
            Create("support-routines", $$"""{"routineId":"editor_deck","gambitIds":["{{gambitId}}"]}"""),
            Create("supports", $$"""
                {"supportId":"editor_deck","countsForOutcome":false,"ownerActivationInterval":1,
                "entityDefinitionId":"{{entityId}}","routineIds":["editor_deck"],"defaultRoutineId":"editor_deck"}
                """),
            Create("support-loadout-policies", """
                {"policyId":"editor_deck","supportIds":["editor_deck"],"editWindows":["Preparation"]}
                """));
        using var preview = await Send(client, setting, page.SourceVersion, create, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var applied = await Apply(client, setting, page.SourceVersion, create);
        var attributes = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/character-attributes?q=editor_deck");
        Assert.Equal("editor_deck", Assert.Single(attributes!.Items).Id);

        using var invalid = await Send(client, setting, applied.SourceVersion, Change(Op("card-pools",
            """{"poolId":"editor_deck","explicitCardIds":["missing_card_for_editor"]}""", CardAuthoringOperationType.Update)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        using var referenced = await Send(client, setting, applied.SourceVersion,
            Change(Op("deck-modules", null, CardAuthoringOperationType.Delete)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, referenced.StatusCode);
        var edited = await Apply(client, setting, applied.SourceVersion, Change(Op("card-pools",
            $$"""{"poolId":"editor_deck","explicitCardIds":["{{cardId}}"],"rarityWeights":{"Common":9} }""",
            CardAuthoringOperationType.Update)));
        var pool = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/card-pools/editor_deck");
        Assert.Equal(9, pool!.Source.GetProperty("rarityWeights").GetProperty("Common").GetInt32());

        var kinds = create.Operations.Select(operation => operation!.Kind).ToArray();
        await Apply(client, setting, edited.SourceVersion, Change(kinds.Select(kind => Op(kind, null, CardAuthoringOperationType.Delete)).ToArray()));
        using var missing = await client.GetAsync(root + "/definitions/card-pools/editor_deck");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash", "uno_classic", "uno_red_0")]
    [InlineData("circular-table", "table_mode", "t_red_0")]
    public async Task RunAuthoringValidatesWritesAndDeletes(string setting, string modeId, string cardId)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        var root = Setting(setting);
        var page = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/runs");
        Assert.NotEmpty(page!.Items);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/runs/schema");
        Assert.Equal("resources", schema!.References["/startingResources/@key"]);
        Assert.Equal("shops", (string?)schema.Schema["x-authoring-dependent-references"]?["/mapNodes/*/activity/definitionId"]?["kinds"]?["Shop"]);
        var challengeSchema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/daily-challenges/schema");
        Assert.Equal("uint64", (string?)challengeSchema!.Schema["properties"]?["seed"]?["format"]);

        static CardAuthoringOperation Op(string kind, string? source, CardAuthoringOperationType operation) => new()
        {
            Kind = kind, DefinitionId = "editor_run", Operation = operation,
            Definition = source is null ? null : JsonSerializer.Deserialize<JsonElement>(source)
        };
        static CardAuthoringOperation Create(string kind, string source) => Op(kind, source, CardAuthoringOperationType.Create);
        var create = Change(
            Create("runs", $$"""
                {"runId":"editor_run","startingResources":{"gold":10},"startingCards":["{{cardId}}","{{cardId}}"],
                "mapNodes":[{"nodeId":"start","completionPolicy":"Required","activity":{"type":"Shop","definitionId":"editor_run"} }]}
                """),
            Create("run-progression-policies", """{"progressionPolicyId":"editor_run","endOfMap":"Complete"}"""),
            Create("profile-progress-policies", $$"""
                {"profileProgressPolicyId":"editor_run","contributingModeIds":["{{modeId}}"],
                "unlocks":[{"unlockId":"bonus","targets":[{"kind":"Card","definitionId":"{{cardId}}"}],
                "condition":{"kind":"RunCompleted","runDefinitionId":"editor_run"} }]}
                """),
            Create("daily-challenges", $$"""
                {"challengeId":"editor_run","configName":"{{setting}}","runDefinitionId":"editor_run","modeId":"{{modeId}}",
                "seed":18446744073709551615,"scoreResourceWeights":{"gold":2} }
                """),
            Create("dialogues", """
                {"dialogueId":"editor_run","title":{"text":"Meeting"},"startNodeId":"start",
                "nodes":[{"nodeId":"start","speaker":{"text":"Host"},"text":{"text":"Welcome"},"choices":[{"choiceId":"leave","text":{"text":"Leave"}}]}]}
                """),
            Create("preparations", $$"""{"preparationId":"editor_run","options":[{"optionId":"supply","grantedCardIds":["{{cardId}}"]}]}"""),
            Create("shops", $$"""{"shopId":"editor_run","items":[{"itemId":"first","cardId":"{{cardId}}"}]}"""),
            Create("relics", """{"relicId":"editor_run","stackLimit":1}"""),
            Create("boards", """{"name":"Board"}"""), Create("zones", """{"name":"Zone"}"""),
            Create("run-events", """{"name":"Event"}"""));
        using var preview = await Send(client, setting, page.SourceVersion, create, "/changes/validate");
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var applied = await Apply(client, setting, page.SourceVersion, create);

        using var referenced = await Send(client, setting, applied.SourceVersion,
            Change(Op("shops", null, CardAuthoringOperationType.Delete)), "/changes/validate");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, referenced.StatusCode);
        var edited = await Apply(client, setting, applied.SourceVersion,
            Change(Op("relics", """{"relicId":"editor_run","stackLimit":2}""", CardAuthoringOperationType.Update)));
        var relic = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/relics/editor_run");
        Assert.Equal(2, relic!.Source.GetProperty("stackLimit").GetInt32());
        var challenge = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/daily-challenges/editor_run");
        Assert.Equal(ulong.MaxValue, challenge!.Source.GetProperty("seed").GetUInt64());

        var kinds = create.Operations.Select(operation => operation!.Kind).ToArray();
        await Apply(client, setting, edited.SourceVersion, Change(kinds.Select(kind => Op(kind, null, CardAuthoringOperationType.Delete)).ToArray()));
        using var missing = await client.GetAsync(root + "/definitions/runs/editor_run");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("uno-clash")]
    [InlineData("circular-table")]
    public async Task NewWritablePackageLinksAtomicallyToExistingSetting(string setting)
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        const string root = "/api/v1/admin/authoring/workspace";
        const string package = "designer-content";
        var manifest = Path.Combine(factory.PackageRoot, package, "package.json");
        var detail = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/settings/" + setting);
        var original = JsonNode.Parse(detail!.Source.GetRawText())!.AsObject();
        var definition = original.DeepClone().AsObject();
        definition["packages"]!.AsArray().Add(new JsonObject { ["packageId"] = package, ["versionRange"] = "1.0.0" });
        definition["authoringPackageId"] = package;
        var newSetting = definition.DeepClone().AsObject();
        newSetting["settingId"] = "designer-setting";
        var catalog = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/settings");
        CardAuthoringChangeRequest Create(string version) => Change(
            new() { Kind = "packages", DefinitionId = package, Operation = CardAuthoringOperationType.Create,
                Definition = JsonSerializer.SerializeToElement(new { packageId = package, version }) },
            new() { Kind = "settings", DefinitionId = setting, Operation = CardAuthoringOperationType.Update,
                Definition = JsonSerializer.SerializeToElement(definition) },
            new() { Kind = "settings", DefinitionId = "designer-setting", SourcePackage = setting,
                Operation = CardAuthoringOperationType.Create, Definition = JsonSerializer.SerializeToElement(newSetting) });
        async Task<HttpResponseMessage> Request(string etag, CardAuthoringChangeRequest change, bool preview = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, root + "/changes" + (preview ? "/validate" : ""))
                { Content = JsonContent.Create(change) };
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        }
        async Task<CardAuthoringChangeReceipt> Save(string etag, CardAuthoringChangeRequest change)
        {
            using var response = await Request(etag, change);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!;
        }

        using var invalid = await Request(catalog!.SourceVersion, Create("invalid"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.False(File.Exists(manifest));
        var unchanged = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/settings/" + setting);
        Assert.Equal(original.ToJsonString(), JsonNode.Parse(unchanged!.Source.GetRawText())!.ToJsonString());
        var change = Create("1.0.0");
        using var preview = await Request(catalog.SourceVersion, change, preview: true);
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.False(File.Exists(manifest));
        var saved = await Save(catalog.SourceVersion, change);
        Assert.True(File.Exists(manifest));
        var settings = await client.GetFromJsonAsync<CardAuthoringSettings>("/api/v1/admin/authoring/settings");
        Assert.Contains(settings!.Items, item => item.SettingId == setting && item.WritablePackageId == package);
        Assert.Contains(settings.Items, item => item.SettingId == "designer-setting" && item.WritablePackageId == package);
        var undone = await Save(saved.SourceVersion, new() { ChangeId = Guid.NewGuid(), UndoOf = saved.ChangeId });
        Assert.False(File.Exists(manifest));
        unchanged = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/settings/" + setting);
        Assert.Equal(original.ToJsonString(), JsonNode.Parse(unchanged!.Source.GetRawText())!.ToJsonString());
        var redone = await Save(undone.SourceVersion, new() { ChangeId = Guid.NewGuid(), RedoOf = undone.ChangeId });
        Assert.True(File.Exists(manifest));
        var authored = await Apply(client, setting, redone.SourceVersion, Change(new CardAuthoringOperation
        {
            Kind = "keywords", DefinitionId = "designer_keyword", Operation = CardAuthoringOperationType.Create,
            Definition = JsonSerializer.SerializeToElement(new { name = "Designer keyword" })
        }));
        var keywordsFile = Assert.Single(authored.ChangedFiles, path => path.StartsWith(package + "/Resources/", StringComparison.Ordinal));
        Assert.Contains("designer_keyword", await File.ReadAllTextAsync(Path.Combine(factory.PackageRoot, keywordsFile)));
    }

    [Fact]
    public async Task WorkspaceAuthoringValidatesActivatesAndUndoesCompositionAcrossSettings()
    {
        using var factory = new WriteFactory();
        using var client = Open(factory);
        const string root = "/api/v1/admin/authoring/workspace";
        const string package = "editor-package";
        var packageFolder = Path.Combine(factory.PackageRoot, package);
        var asset = Path.Combine(packageFolder, "Resources", "keywords.json");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllTextAsync(asset, """{"editor_keyword":{"name":"Test"}}""");
        var manifest = Path.Combine(packageFolder, "package.json");
        var settings = await client.GetFromJsonAsync<AuthoringPage<CardAuthoringSummary>>(root + "/definitions/settings");
        Assert.Contains(settings!.Items, item => item.DefinitionId == "uno-clash" && item.CanEdit && !item.CanDelete);
        var schema = await client.GetFromJsonAsync<AuthoringKindSchema>(root + "/kinds/settings/schema");
        Assert.Equal("package-ids", schema!.References["/packages/*/packageId"]);
        Assert.Equal("local:packages/packageId", schema.References["/authoringPackageId"]);
        var dirs = await client.GetFromJsonAsync<AuthoringPage<AuthoringReference>>(root + "/references/package-sources");
        Assert.Contains(dirs!.Items, item => item.Id == "circular-table");

        async Task<HttpResponseMessage> Request(string version, CardAuthoringChangeRequest change, bool preview = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, root + "/changes" + (preview ? "/validate" : ""))
                { Content = JsonContent.Create(change) };
            request.Headers.TryAddWithoutValidation("If-Match", version);
            return await client.SendAsync(request);
        }
        async Task<CardAuthoringChangeReceipt> Save(string version, CardAuthoringChangeRequest change)
        {
            using var response = await Request(version, change);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CardAuthoringChangeReceipt>())!;
        }
        static CardAuthoringOperation Op(string kind, string id, CardAuthoringOperationType operation, object? definition = null, string? owner = null) =>
            new() { Kind = kind, DefinitionId = id, Operation = operation, SourcePackage = owner,
                Definition = definition is null ? null : JsonSerializer.SerializeToElement(definition) };
        CardAuthoringOperation NewSetting(string id, string source) => Op("settings", id, CardAuthoringOperationType.Create, new
        {
            settingId = id, displayName = "Test setting",
            packages = new[] { new { packageId = $"heroscript.{source}" } }
        }, package);
        var create = Change(
            Op("packages", package, CardAuthoringOperationType.Create, new { packageId = "fixture.editor", version = "1.0.0" }),
            NewSetting("editor-uno", "uno-clash"), NewSetting("editor-table", "circular-table"));
        using var preview = await Request(settings.SourceVersion, create, preview: true);
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.False(File.Exists(manifest));
        var created = await Save(settings.SourceVersion, create);
        Assert.Equal(new[] { "editor-table", "editor-uno" }, created.ActiveRevisions.Keys.Order(StringComparer.Ordinal));
        Assert.True(File.Exists(manifest));
        var catalog = await client.GetFromJsonAsync<CardAuthoringSettings>("/api/v1/admin/authoring/settings");
        Assert.Contains(catalog!.Items, item => item.SettingId == "editor-table" && item.WritablePackageId == "fixture.editor");
        var tableKinds = await client.GetFromJsonAsync<AuthoringKind[]>("/api/v1/admin/authoring/settings/editor-table/kinds");
        Assert.Contains(tableKinds!, item => item.Kind == "cards" && item.CanCreate);
        var settingDetail = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/settings/editor-uno");
        var settingDocument = JsonNode.Parse(settingDetail!.Source.GetRawText())!.AsObject();
        Assert.Equal("fixture.editor", (string?)settingDocument["authoringPackageId"]);
        settingDocument["authoringPackageId"] = "missing_package";
        using var invalidOwner = await Request(created.SourceVersion,
            Change(Op("settings", "editor-uno", CardAuthoringOperationType.Update, settingDocument)), preview: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidOwner.StatusCode);
        settingDocument["authoringPackageId"] = "heroscript.uno-clash";
        created = await Save(created.SourceVersion,
            Change(Op("settings", "editor-uno", CardAuthoringOperationType.Update, settingDocument)));
        catalog = await client.GetFromJsonAsync<CardAuthoringSettings>("/api/v1/admin/authoring/settings");
        Assert.Contains(catalog!.Items, item => item.SettingId == "editor-uno" && item.WritablePackageId == "heroscript.uno-clash");

        var edit = Change(Op("packages", package, CardAuthoringOperationType.Update, new
        {
            packageId = "fixture.editor", version = "1.1.0",
            content = new[] { new { kind = "keywords", operation = "Definition", sourcePath = "Resources/keywords.json", artifactPath = "keywords/editor.json" } }
        }));
        using var stale = await Request(settings.SourceVersion, edit);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var edited = await Save(created.SourceVersion, edit);
        Assert.Equal(2, edited.ActiveRevisions.Count);
        var detail = await client.GetFromJsonAsync<AuthoringDefinitionDetail>(root + "/definitions/packages/editor-package");
        Assert.Equal("1.1.0", detail!.Source.GetProperty("version").GetString());
        var deletePackage = Op("packages", package, CardAuthoringOperationType.Delete);
        using var rejected = await Request(edited.SourceVersion, Change(deletePackage), preview: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var removed = await Save(edited.SourceVersion, Change(deletePackage,
            Op("settings", "editor-uno", CardAuthoringOperationType.Delete),
            Op("settings", "editor-table", CardAuthoringOperationType.Delete)));
        Assert.False(File.Exists(manifest));
        Assert.True(File.Exists(asset));
        var manifests = factory.Services.GetRequiredService<Core.Content.IContentManifestProvider>();
        Assert.True(manifests.GetManifest("editor-uno").IsFailure);
        Assert.True(manifests.GetByRevision(edited.ActiveRevisions["editor-uno"]).IsSuccess);
        var restored = await Save(removed.SourceVersion, new() { ChangeId = Guid.NewGuid(), UndoOf = removed.ChangeId });
        Assert.Equal(edited.ActiveRevisions["editor-uno"], restored.ActiveRevisions["editor-uno"]);
        Assert.True(File.Exists(manifest));
        await factory.DisposeAsync();
        using var restarted = new WriteFactory(factory.PersistenceRoot);
        using var afterRestart = Open(restarted);
        var persisted = await afterRestart.GetFromJsonAsync<CardAuthoringSettings>("/api/v1/admin/authoring/settings");
        Assert.Contains(persisted!.Items, item => item.SettingId == "editor-uno" && item.WritablePackageId == "heroscript.uno-clash");
        Assert.Contains(persisted.Items, item => item.SettingId == "editor-table" && item.WritablePackageId == "fixture.editor");
        await Apply(afterRestart, "editor-table", restored.SourceVersion, Change(new CardAuthoringOperation
        {
            Kind = "keywords", DefinitionId = "new_authoring_keyword", Operation = CardAuthoringOperationType.Create,
            Definition = JsonSerializer.SerializeToElement(new { name = "Authored after restart" })
        }));
        Assert.Contains("new_authoring_keyword", await File.ReadAllTextAsync(asset));
    }

    private static async Task<Guid> StartRun(HttpClient client, string settingId, string runDefinitionId, string modeId,
        string playerEntityId, string contentRevision, ulong seed)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId, runDefinitionId, modeId, playerEntityId, contentRevision, seed
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var run = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(contentRevision, run.GetProperty("contentRevision").GetString());
        return run.GetProperty("runId").GetGuid();
    }

    private static async Task AssertRunRevision(HttpClient client, Guid runId, string revision)
    {
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}");
        Assert.Equal(revision, run.GetProperty("contentRevision").GetString());
    }

    private static async Task<JsonElement> ReadPublishedCard(HttpClient client, string setting, string card, string revision)
    {
        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/content/cards/{card}?configName={setting}&revision={revision}");
        return result.GetProperty("definition");
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
        private readonly bool _performanceDiagnostics;

        public WriteFactory(string? root = null, AuthoringWriteCheckpoint? stop = null,
            bool includeEmptyPackage = false, bool applyEnabled = true, string? packageSource = null,
            bool performanceDiagnostics = false)
            : base(root ?? Path.Combine(Path.GetTempPath(), "HeroScript", "authoring-write-tests", Guid.NewGuid().ToString("N")))
        {
            _stop = stop;
            _includeEmptyPackage = includeEmptyPackage;
            _applyEnabled = applyEnabled;
            _performanceDiagnostics = performanceDiagnostics;
            PackageRoot = Path.Combine(PersistenceRoot, "packages");
            if (root is null)
            {
                CardAuthoringWorkspaceTests.WorkspaceFactory.CopyPackages(PackageRoot, packageSource);
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
            builder.UseSetting("Performance:DiagnosticsEnabled", _performanceDiagnostics ? "true" : "false");
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
