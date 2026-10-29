using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Content.Migrations;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResponseContentValidationTests
{
    [Fact]
    public void CapturedGraphValidatesResponseBodiesAndTypedNumericReferencesWithoutChangingBundle()
    {
        var bundle = Bundle(Response());
        var before = CanonicalJson.ComputeHash(bundle);
        var result = Validator().Validate(bundle);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(before, CanonicalJson.ComputeHash(bundle));
    }

    [Theory]
    [InlineData("pipeline", "missing")]
    [InlineData("resource", "missing")]
    [InlineData("reference", "unknown")]
    [InlineData("alias", "unknown")]
    [InlineData("timing", "boundary")]
    [InlineData("availability", "card")]
    public void UnsupportedOrUnreachableResponseFailsInContentValidation(string mutation, string error)
    {
        var response = Response();
        response = mutation switch
        {
            "pipeline" => response with { Effects = [response.Effects[0] with { Parameters = [response.Effects[0].Parameters[0] with { PipelineId = "missing" }] }] },
            "resource" => response with { Observe = response.Observe with { Resource = new() { ResourceIds = ["missing"] } } },
            "reference" => response with { Condition = "event.change.unknown" },
            "alias" => response with { Condition = "stacks" },
            "timing" => response with { Timing = ResponseExecutionTiming.ContainerBoundary },
            _ => response with { Availability = new() { Kind = ResponseAvailabilityKind.SelectedCard } }
        };
        var result = Validator().Validate(Bundle(response));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, message => message.Contains(error, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NumericProjectionUsesCanonicalPercentAndSupportsNamespacedResourceIds()
    {
        var context = GameplayResponseCompilationTests.Context();
        var actor = context.Combat!.GetActor("hero")!;
        var definition = actor.GetResource("focus")!.Definition! with { ResourceId = "combat.focus" };
        actor = actor with { ResourceState = actor.ResourceState with { Resources = actor.ResourceState.Resources.ToDictionary()
            .Append(new KeyValuePair<string, Core.Resources.ResourcePool>("combat.focus", Core.Resources.ResourcePool.Materialize(definition, 5, 20))).ToDictionary() } };
        context = context with { Combat = context.Combat.ReplaceActor(actor) };
        var values = GameplayResponseNumbers.Read(new() { Payload = new Core.GameplayEvents.ActionOccurrencePayload { ActionDefinitionId = "test" } },
            GameplayResponseCompilationTests.Registration(), context, "hero", ["owner.resources.combat.focus.current", "owner.resources.combat.focus.percent"]);
        Assert.True(values.IsSuccess, values.IsFailure ? values.Error : null);
        Assert.Equal(5, values.Value["owner.resources.combat.focus.current"]);
        Assert.Equal(actor.GetResource("combat.focus")!.GetPercentage(), values.Value["owner.resources.combat.focus.percent"]);
        Assert.True(GameplayResponseNumbers.SupportsReference("owner.resources.combat.focus.percent", id => id == "combat.focus"));
        Assert.False(GameplayResponseNumbers.SupportsReference("event.change.unknown", _ => true));
    }

    [Fact]
    public void StatusCannotAdvertiseACombatBoundaryThatOnlyTheRelicLifecycleSupports()
    {
        var context = GameplayResponseCompilationTests.Context();
        var definition = new RelicDefinition { RelicId = "test", Triggers = [new() { TriggerId = "entry", Boundary = "CombatStart",
            Effects = [new() { Type = EffectType.REMOVE_STATUS, StatusId = "absent" }] }] };
        var converted = GameplayBoundaryResponseMigration.Relic(definition, context.Catalog);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error : null);
        var status = new Core.StatusEffects.StatusEffectInstance { StatusId = "test", InstanceId = Guid.Parse("4c1371ee-2925-4081-9691-b357dd6f904f"),
            IsActive = true, TargetId = "hero", ContentRevision = "revision", Definition = new() { StatusId = "test", Responses = converted.Value.Responses } };
        var result = GameplayBoundarySources.Status(context.Run, context.Combat!, [status]);
        Assert.True(result.IsFailure); Assert.Contains("boundary", result.Error);
        Assert.True(GameplayBoundaryResponseMigration.Status(status.Definition with { Responses = null, Triggers = definition.Triggers }, context.Catalog).IsFailure);
    }

    private static GameplayResponseDefinition Response() => GameplayResponseCompilationTests.Definition() with {
        Condition = "event.change.applied_delta", Effects = [EffectSequenceBudgetTests.Damage(1, 1)] };
    private static ContentGraphValidator Validator() => new(new ContentKindRegistry(ContentKindRegistry.Default.Kinds,
        allowShadowResponseDeclarations: true));
    private static ContentBundle Bundle(GameplayResponseDefinition response)
    {
        var bundle = EffectSequenceBudgetTests.Bundle(EffectSequenceBudgetTests.Profiles());
        var artifacts = bundle.Artifacts.SetItem("relics/test.json", JsonSerializer.SerializeToElement(new Dictionary<string, RelicDefinition> {
            ["test"] = new() { RelicId = "test", Responses = [response] } }));
        return bundle with { Artifacts = artifacts, Manifest = bundle.Manifest with { Artifacts = artifacts.Select(item => new ContentArtifactManifest {
            Path = item.Key, Kind = item.Key.Split('/')[0], DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray() } };
    }
}
