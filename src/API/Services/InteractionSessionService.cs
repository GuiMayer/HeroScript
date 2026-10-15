using System.Collections.Immutable;
using System.Text.Json;
using API.Models;
using API.Models.Combat;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.LegalActions;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.Projections;

namespace API.Services;

public sealed record InteractionSession(InteractionVersion Version, JsonElement Run, JsonElement Combat,
    JsonElement Commands, JsonElement Capabilities, JsonElement CardZones, JsonElement Attributes,
    JsonElement CardPresentations, JsonElement ModifierItems, JsonElement Actions, JsonElement Evaluations);
public sealed record InteractionSessionRead(string ETag, InteractionSession? Session);

/// <summary>One captured snapshot; projections never re-enter HTTP or reload the run.</summary>
public sealed class InteractionSessionService(IRunQueryService runs, ICardPresentationQueryService cards,
    ICardModifierItemQueryService items, IActorAttributeProjectionService attributes,
    ILegalActionResolver legal, IToolAccessPolicy access)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public Result<InteractionSessionRead> Read(Guid runId, int? expectedSequence, string? ifNoneMatch)
    {
        using var hashes = CanonicalJson.BeginHashScope();
        using var timing = Core.Diagnostics.OperationalTiming.Measure("session_projection");
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return Result<InteractionSessionRead>.Failure(loaded.Error);
        var run = loaded.Value;
        if (expectedSequence.HasValue && expectedSequence != run.Sequence)
            return Result<InteractionSessionRead>.Failure("VERSION_CONFLICT: The session revision has advanced");
        var context = new InteractionReadContext(run);
        var granted = access.EffectiveFor(run);
        var etag = "\"" + CanonicalJson.ComputeHash(new { context.Version, granted, access.Snapshot.Profile, schema = 1 }) + "\"";
        if (ifNoneMatch == etag) return Result<InteractionSessionRead>.Success(new(etag, null));
        var combat = run.GetActiveEncounter()?.Combat;
        var commands = runs.GetAvailableCommands(run);
        if (commands.IsFailure) return Result<InteractionSessionRead>.Failure(commands.Error);
        var actor = combat?.PriorityWindow?.HolderActorId ?? combat?.ActivationState?.ActiveActorId;
        var facts = cards.Read(run, combat != null, combat, actor,
            access.Allows(run, ToolCapabilities.CardInspectFull) ? InspectionDetailLevel.Full : InspectionDetailLevel.Resolved);
        if (facts.IsFailure) return Result<InteractionSessionRead>.Failure(facts.Error);
        var inventory = items.Read(run);
        if (inventory.IsFailure) return Result<InteractionSessionRead>.Failure(inventory.Error);
        var values = attributes.Project(run, combat, run.PlayerEntityId);
        if (values.IsFailure) return Result<InteractionSessionRead>.Failure(values.Error);
        var actions = ImmutableArray<LegalActionCandidate>.Empty;
        var evaluations = ImmutableArray.CreateBuilder<CardInspectionResult>();
        if (combat != null && combat.Status == Core.Combat.Models.CombatStatus.ACTIVE && actor != null)
        {
            var origin = combat.GetActor(actor) is { } active && combat.ControllerOf(active) == Core.Combat.Models.ControllerKind.AI
                ? CombatCommandOrigin.AutomaticController : CombatCommandOrigin.PlayerInput;
            var options = legal.Resolve(run, combat, actor, origin);
            if (options.IsFailure) return Result<InteractionSessionRead>.Failure(options.Error);
            actions = options.Value.Candidates.ToImmutableArray();
        }
        return Result<InteractionSessionRead>.Success(new(etag, new(context.Version,
            RunInteractionProjection.Project(context, granted.Contains(ToolCapabilities.CheatCardZones)),
            Json(combat == null ? null : CombatStateResponse.From(combat)),
            Json(new { run.RunId, run.Sequence, run.Determinism.Step, commands = commands.Value }),
            Json(new { run.RunId, profile = access.Snapshot.Profile, granted }),
            Json(CardZoneReadModel.Project(run)), Json(values.Value), Json(facts.Value), Json(inventory.Value),
            Json(new { candidates = actions.Select(value => new {
                value.CandidateId, value.Source, value.Command, value.ActionId, value.CardDefinitionId,
                value.Costs, value.Applications, value.OutcomeUncertain, value.ReactionTransition,
                value.ResolutionFingerprint
            }) }), Json(new { cards = evaluations.ToImmutable() }))));
    }
}
