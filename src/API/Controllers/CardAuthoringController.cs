using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using API.Attributes;
using API.Contracts;
using API.Services;
using Core.Content;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[AdminEndpoint]
[Route("api/v1/admin/authoring")]
public sealed class CardAuthoringController(
    CardAuthoringService authoring, CardAuthoringWriter writer, AuthoringWriteCoordinator coordinator,
    IContentKindRegistry kinds, ILogger<CardAuthoringController> logger)
    : BaseApiController(logger)
{
    private const string SettingRoute = "settings/{settingId}";
    private const string Cards = "cards";
    private static readonly Lazy<JsonNode> CardSchema = new(CreateCardSchema);
    private static readonly HashSet<string> TagCriteria =
        new(["requiredTags", "excludedTags", "includeTags", "excludeTags"], StringComparer.OrdinalIgnoreCase);

    [HttpGet("capabilities")]
    public AuthoringCapabilities Capabilities() =>
        new("1", !coordinator.RecoveryRequired, CanApply, CanApply, CanApply);

    private bool CanApply => coordinator.Enabled && !coordinator.RecoveryRequired;

    [HttpGet("settings")]
    [ProducesResponseType(typeof(CardAuthoringSettings), StatusCodes.Status200OK)]
    public Task<IActionResult> Settings(CancellationToken cancellationToken) =>
        coordinator.ExecuteAsync<IActionResult>(async () =>
    {
        if (coordinator.RecoveryRequired)
            return ApiProblem(503, AuthoringErrorCodes.RecoveryRequired, "Authoring workspace recovering",
                "Restart the engine to recover the interrupted write.");
        var catalog = await authoring.DiscoverAsync(cancellationToken);
        return Ok(new CardAuthoringSettings(catalog.Settings.Select(item => new CardAuthoringSetting(
            item.Definition.SettingId, item.Definition.DisplayName, authoring.WritablePackage(item.Definition))).ToArray(),
            catalog.Diagnostics));
    }, cancellationToken);

    [HttpGet(SettingRoute + "/kinds")]
    [ProducesResponseType(typeof(AuthoringKind[]), StatusCodes.Status200OK)]
    public Task<IActionResult> Kinds(string settingId, CancellationToken cancellationToken) =>
        WithView(settingId, cancellationToken, (_, view) => Task.FromResult<IActionResult>(Ok(
            kinds.Kinds.Select(kind => new AuthoringKind(kind.Kind, kind.Kind == Cards,
                kind.Kind == Cards && view.Writable is not null)).ToArray())));

    [HttpGet(SettingRoute + "/kinds/{kind}/schema")]
    [ProducesResponseType(typeof(AuthoringKindSchema), StatusCodes.Status200OK)]
    public Task<IActionResult> Schema(string settingId, string kind, CancellationToken cancellationToken) =>
        WithView(settingId, cancellationToken, (_, _) => Task.FromResult<IActionResult>(kind == Cards
            ? Ok(new AuthoringKindSchema(Cards, CardSchema.Value, new Dictionary<string, string>
            {
                ["/tags"] = "tags",
                ["/componentBundles/*/bundleId"] = "card-component-bundles"
            }))
            : ApiNotFound($"Authoring schema not available for '{kind}'.")));

    [HttpGet(SettingRoute + "/definitions/cards")]
    [ProducesResponseType(typeof(AuthoringPage<CardAuthoringSummary>), StatusCodes.Status200OK)]
    public Task<IActionResult> CardsPage(string settingId, [FromQuery] string? q, [FromQuery] string? cursor,
        CancellationToken cancellationToken, [FromQuery] int limit = 50) =>
        WithView(settingId, cancellationToken, (workspace, view) => Task.FromResult(Paginate(
            view.Cards.Values.Where(card => Matches(card.Id, q) ||
                    card.Effective.TryGetProperty("metadata", out var metadata) &&
                    metadata.ValueKind == JsonValueKind.Object &&
                    metadata.TryGetProperty("displayName", out var name) && name.ValueKind == JsonValueKind.String &&
                    Matches(name.GetString()!, q))
                .Select(Summary),
            card => card.DefinitionId, workspace.ETag, settingId, Cards, q, cursor, limit)));

    [HttpGet(SettingRoute + "/definitions/cards/{definitionId}")]
    [ProducesResponseType(typeof(CardAuthoringDefinition), StatusCodes.Status200OK)]
    public Task<IActionResult> Card(string settingId, string definitionId, CancellationToken cancellationToken) =>
        WithView(settingId, cancellationToken, async (workspace, view) =>
        {
            if (!view.Cards.TryGetValue(definitionId, out var card))
                return ApiNotFound($"Card '{definitionId}' not found.");
            var source = await authoring.ReadSourceAsync(workspace, card, cancellationToken);
            if (source.IsFailure)
                return ApiProblem(422, AuthoringErrorCodes.InvalidCandidate, "Card source unavailable", source.Error);
            var artifact = card.Entry.ArtifactPath!.Replace('\\', '/');
            var provenance = view.Compilation.Provenance.Where(item => item.ArtifactPath == artifact &&
                (item.DefinitionId is null || item.DefinitionId == definitionId)).ToArray();
            return Ok(new CardAuthoringDefinition(Summary(card), source.Value, card.Effective, provenance));
        });

    [HttpGet(SettingRoute + "/references/tags")]
    [ProducesResponseType(typeof(AuthoringPage<CardAuthoringTag>), StatusCodes.Status200OK)]
    public Task<IActionResult> Tags(string settingId, [FromQuery] string? q, [FromQuery] string? cursor,
        CancellationToken cancellationToken, [FromQuery] int limit = 50) =>
        WithView(settingId, cancellationToken, (workspace, view) =>
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in view.Cards.Values)
            {
                foreach (var property in card.Effective.EnumerateObject())
                    if (property.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))
                        AddTags(property.Value, declared);
            }
            foreach (var artifact in view.Compilation.Bundle.Artifacts.Values)
                CollectTagCriteria(artifact, referenced);
            var tags = declared.Union(referenced, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Where(tag => Matches(tag, q))
                .Select(tag => new CardAuthoringTag(tag, declared.Contains(tag), referenced.Contains(tag)));
            return Task.FromResult(Paginate(tags, tag => tag.Id, workspace.ETag, settingId, "tags", q, cursor, limit));
        });

    [HttpPost(SettingRoute + "/changes/validate")]
    [ProducesResponseType(typeof(CardAuthoringValidation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Validate(string settingId, [FromBody] CardAuthoringChangeRequest request,
        CancellationToken cancellationToken)
    {
        var sourceVersion = Request.Headers.IfMatch;
        if (sourceVersion.Count == 0)
            return Task.FromResult<IActionResult>(ApiProblem(428, AuthoringErrorCodes.PreconditionRequired,
                "Source version required", "Supply the source ETag in If-Match."));
        return WithView(settingId, cancellationToken, async (workspace, view) =>
        {
            if (sourceVersion.Count != 1 || !string.Equals(sourceVersion[0], workspace.ETag, StringComparison.Ordinal))
                return ApiProblem(412, AuthoringErrorCodes.SourceChanged, "Source version changed", "Reload the authoring source and retry.");

            return ApplyResult(await writer.PreviewAsync(workspace, view, request, cancellationToken));
        });
    }

    [HttpPost(SettingRoute + "/changes")]
    [ProducesResponseType(typeof(CardAuthoringChangeReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Apply(string settingId, [FromBody] CardAuthoringChangeRequest request,
        CancellationToken cancellationToken) =>
        ApplyChangeAsync(settingId, request, cancellationToken);

    [HttpPost(SettingRoute + "/changes/{changeId:guid}/undo")]
    [ProducesResponseType(typeof(CardAuthoringChangeReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Undo(string settingId, Guid changeId, [FromBody] AuthoringCompensationRequest request,
        CancellationToken cancellationToken) =>
        ApplyCompensationAsync(settingId, changeId, request.ChangeId, undo: true, cancellationToken);

    [HttpPost(SettingRoute + "/changes/{changeId:guid}/redo")]
    [ProducesResponseType(typeof(CardAuthoringChangeReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Redo(string settingId, Guid changeId, [FromBody] AuthoringCompensationRequest request,
        CancellationToken cancellationToken) =>
        ApplyCompensationAsync(settingId, changeId, request.ChangeId, undo: false, cancellationToken);

    private Task<IActionResult> ApplyCompensationAsync(string settingId, Guid target, Guid changeId, bool undo,
        CancellationToken cancellationToken)
    {
        if (target == Guid.Empty || changeId == Guid.Empty || target == changeId)
            return Task.FromResult<IActionResult>(ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid change ID",
                "Supply a non-empty change ID distinct from the compensation target."));
        return ApplyChangeAsync(settingId, new CardAuthoringChangeRequest
        {
            ChangeId = changeId,
            UndoOf = undo ? target : null,
            RedoOf = undo ? null : target
        }, cancellationToken);
    }

    private async Task<IActionResult> ApplyChangeAsync(string settingId, CardAuthoringChangeRequest request,
        CancellationToken cancellationToken)
    {
        var version = Request.Headers.IfMatch;
        if (version.Count == 0)
            return ApiProblem(428, AuthoringErrorCodes.PreconditionRequired, "Source version required",
                "Supply the source ETag in If-Match.");
        if (version.Count != 1 || string.IsNullOrWhiteSpace(version[0]))
            return ApiProblem(412, AuthoringErrorCodes.SourceChanged, "Source version changed",
                "Supply one strong workspace ETag in If-Match.");
        return ApplyResult(await writer.ApplyAsync(settingId, version[0]!, request, cancellationToken));
    }

    [HttpGet(SettingRoute + "/changes/{changeId:guid}")]
    [ProducesResponseType(typeof(CardAuthoringChangeReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(string settingId, Guid changeId, CancellationToken cancellationToken) =>
        ApplyResult(await writer.GetAsync(settingId, changeId, cancellationToken));

    private IActionResult ApplyResult(AuthoringApplyResult result)
    {
        Response.Headers.CacheControl = "no-store";
        if (result.Status == 200)
        {
            if (result.Receipt is { State: "applied" } receipt)
                Response.Headers.ETag = receipt.SourceVersion;
            return result.Validation is { } validation ? Ok(validation) : Ok(result.Receipt);
        }
        var problem = ApiProblemDetailsFactory.Create(HttpContext, result.Status, result.Code!, "Authoring change rejected", result.Detail!);
        if (result.Validation is not null)
            problem.Extensions["validation"] = result.Validation;
        if (result.Receipt is not null)
            problem.Extensions["receipt"] = result.Receipt;
        return new ObjectResult(problem) { StatusCode = result.Status, ContentTypes = { "application/problem+json" } };
    }

    private Task<IActionResult> WithView(string settingId, CancellationToken cancellationToken,
        Func<CardAuthoringWorkspace, CardAuthoringView, Task<IActionResult>> action) =>
        coordinator.ExecuteAsync(() => ReadView(settingId, cancellationToken, action), cancellationToken);

    private async Task<IActionResult> ReadView(string settingId, CancellationToken cancellationToken,
        Func<CardAuthoringWorkspace, CardAuthoringView, Task<IActionResult>> action)
    {
        if (coordinator.RecoveryRequired)
            return ApiProblem(503, AuthoringErrorCodes.RecoveryRequired, "Authoring workspace recovering",
                "Restart the engine to recover the interrupted write.");
        var workspace = await authoring.CaptureAsync(cancellationToken);
        if (workspace.IsFailure)
            return ApiProblem(503, ApiErrorCodes.DependencyUnavailable, "Authoring sources unavailable", workspace.Error);
        if (!workspace.Value.Catalog.Settings.Any(item => item.Definition.SettingId == settingId))
            return ApiNotFound($"Setting '{settingId}' not found.");
        var view = await authoring.GetViewAsync(workspace.Value, settingId, cancellationToken);
        if (view.IsFailure)
            return ApiProblem(422, AuthoringErrorCodes.InvalidCandidate, "Setting compilation failed", view.Error);
        Response.Headers.ETag = workspace.Value.ETag;
        Response.Headers.CacheControl = "no-store";
        return await action(workspace.Value, view.Value);
    }

    private IActionResult Paginate<T>(IEnumerable<T> items, Func<T, string> id, string etag,
        string settingId, string kind, string? query, string? cursor, int limit)
    {
        if (limit is < 1 or > 500)
            return ApiBadRequest(ApiErrorCodes.InvalidArgument, "Invalid limit", "Limit must be between 1 and 500.");
        var scope = new PageCursor(etag, settingId, kind, query, "");
        if (cursor is not null)
        {
            PageCursor? decoded;
            try
            {
                decoded = JsonSerializer.Deserialize<PageCursor>(Convert.FromBase64String(cursor));
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                decoded = null;
            }

            if (decoded is null || decoded with { ETag = etag, After = "" } != scope)
                return ApiBadRequest(ApiErrorCodes.InvalidArgument, "Invalid cursor", "The cursor does not match the query.");
            if (decoded.ETag != etag)
                return ApiProblem(409, AuthoringErrorCodes.SourceChanged, "Cursor expired", "Restart pagination from the current source.");
            items = items.Where(item => StringComparer.Ordinal.Compare(id(item), decoded.After) > 0);
        }

        var page = items.Take(limit + 1).ToArray();
        var next = page.Length > limit
            ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(scope with { After = id(page[limit - 1]) }))
            : null;
        return Ok(new AuthoringPage<T>(page.Take(limit).ToArray(), next, etag));
    }

    private static CardAuthoringSummary Summary(CardAuthoringCard card) =>
        new(card.Id, card.Package.Manifest.PackageId, card.CanEdit, card.CanEdit);

    private static bool Matches(string value, string? query) =>
        string.IsNullOrWhiteSpace(query) || value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void CollectTagCriteria(JsonElement element, HashSet<string> tags)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TagCriteria.Contains(property.Name))
                    AddTags(property.Value, tags);
                else if (!property.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                    CollectTagCriteria(property.Value, tags);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CollectTagCriteria(child, tags);
        }
    }

    private static void AddTags(JsonElement value, HashSet<string> tags)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } tag)
                tags.Add(tag);
    }

    private static JsonNode CreateCardSchema()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var schema = options.GetJsonSchemaAsNode(typeof(CardContentDefinition),
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["required"] = new JsonArray(JsonValue.Create("cardId"));
        return schema;
    }

    private sealed record PageCursor(string ETag, string SettingId, string Kind, string? Query, string After);
}
