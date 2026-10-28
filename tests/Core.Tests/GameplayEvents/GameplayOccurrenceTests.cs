using System.Collections.Immutable;
using System.Text.Json;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Resources;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayOccurrenceTests
{
    internal static readonly GameplayPublisherContract Publisher = new("core.resource", HierarchySelector.Compile(["resource.**"]).Value,
        [GameplayPayloadKind.ResourceChange]);
    internal static readonly GameplayEventKindDefinition Kind = new()
    { Kind = "resource.change.resolved", PublisherId = "core.resource", Payload = GameplayPayloadKind.ResourceChange };
    internal static GameplayEventCatalog Catalog() => GameplayEventCatalog.Create("setting", "revision", [Publisher], [Kind],
        [new() { ReasonId = "cost.payment" }, new() { ReasonId = "combat.damage.direct" }]).Value;
    internal static GameplayOccurrence Occurrence() => new()
    {
        EventId = "event-0", RootEventId = "event-0", Kind = Kind.Kind, ReasonId = "cost.payment",
        Scope = new() { SettingId = "setting", ContentRevision = "revision", RunId = "run", BranchId = "main", ActionId = "action", CommandId = "command" },
        Payload = new ResourceChangePayload { ResourceId = "任意リソース", Operation = ResourceMutationOperation.Set,
            Before = 15, Operand = 20, RequestedValue = 20, After = 18 }
    };

    [Fact]
    public void JsonRoundTripPreservesTypedFactsAndIndependentSnapshots()
    {
        var original = Occurrence();
        Assert.True(original.Validate(Catalog()).IsSuccess);
        var encoded = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<GameplayOccurrence>(encoded)!;
        Assert.True(restored.Validate(Catalog()).IsSuccess);
        Assert.Equal(CanonicalJson.ComputeHash(original), CanonicalJson.ComputeHash(restored));
        var change = Assert.IsType<ResourceChangePayload>(restored.Payload);
        Assert.Equal(5, change.RequestedDelta);
        Assert.Equal(3, change.AppliedDelta);
        Assert.Equal(2, change.LimitedDelta);
        Assert.Null(restored.SourceActorId); // Environmental/source-less facts do not invent an actor.
        var altered = restored with { Scope = restored.Scope with { BranchId = "other" }, Tags = ["different"] };
        Assert.Equal("main", original.Scope.BranchId);
        Assert.Empty(original.Tags);
        Assert.NotEqual(CanonicalJson.ComputeHash(original), CanonicalJson.ComputeHash(altered));
    }

    [Fact]
    public void ClampAndAbsorptionAreDifferentFactsAndNoChangeIsValid()
    {
        var clamped = new ResourceChangePayload { ResourceId = "custom", Before = 8, Operand = 12,
            RequestedValue = -4, After = 0, Operation = ResourceMutationOperation.Subtract };
        Assert.True(clamped.Validate().IsSuccess);
        Assert.Equal(-12, clamped.RequestedDelta);
        Assert.Equal(-8, clamped.AppliedDelta);
        var resolvedAfterAbsorption = clamped with { Before = 30, Operand = 8, RequestedValue = 22, After = 22,
            RelatedFactIds = ["settlement-1"] };
        Assert.True(resolvedAfterAbsorption.Validate().IsSuccess);
        Assert.Equal(0, resolvedAfterAbsorption.LimitedDelta);
        Assert.Equal(-8, resolvedAfterAbsorption.RequestedDelta);
        Assert.True((clamped with { Operand = 0, RequestedValue = 8, After = 8 }).Validate().IsSuccess);
        Assert.False((clamped with { Operand = float.NaN }).Validate().IsSuccess);
        Assert.False((clamped with { After = float.PositiveInfinity }).Validate().IsSuccess);
        Assert.False((clamped with { RequestedValue = 10 }).Validate().IsSuccess);
    }

    [Fact]
    public void CatalogRejectsClaimedCoreFactsFromAnUntrustedDomainPublisher()
    {
        var domain = new GameplayPublisherContract("game.sandbox", HierarchySelector.Compile(["game.sandbox.**"]).Value,
            [GameplayPayloadKind.RuleResult]);
        Assert.True(GameplayEventCatalog.Create("s", "r", [domain], [Kind with { PublisherId = domain.PublisherId }], []).IsFailure);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind with { CanOpenResponseWindow = true }], []).IsFailure);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind with { SchemaVersion = 2 }], []).IsFailure);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind, Kind], []).IsFailure);
        var declarations = new[] { Kind };
        var catalog = GameplayEventCatalog.Create("s", "r", [Publisher], declarations, []).Value;
        declarations[0] = Kind with { Kind = "resource.fake" };
        Assert.True(catalog.Kinds.ContainsKey(Kind.Kind));
        Assert.False(catalog.Kinds.ContainsKey("resource.fake"));
    }

    [Fact]
    public void AliasesAreCanonicalizedAtPublicationAndNeverDuplicateOccurrences()
    {
        var catalog = GameplayEventCatalog.Create("setting", "revision", [Publisher], [Kind],
            [new() { ReasonId = "cost.payment" }], [new("legacy.changed", "legacy.intermediate"), new("legacy.intermediate", Kind.Kind)],
            [new("cost.old", "cost.payment")]).Value;
        Assert.Equal(Kind.Kind, catalog.FindKind("legacy.changed").Value.Kind);
        Assert.Equal("cost.payment", catalog.FindReason("cost.old").Value.ReasonId);
        Assert.False((Occurrence() with { Kind = "legacy.changed" }).Validate(catalog).IsSuccess);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind], [],
            [new("alias.one", "alias.two"), new("alias.two", "alias.one")]).IsFailure);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind], [], [new("alias.one", "unknown.kind")]).IsFailure);
        Assert.True(GameplayEventCatalog.Create("s", "r", [Publisher], [Kind], [], [new(Kind.Kind, Kind.Kind)]).IsFailure);
    }

    [Fact]
    public void OccurrenceRejectsWrongScopePayloadTagsAndCausalReferences()
    {
        var original = Occurrence();
        foreach (var invalid in new[]
        {
            original with { Scope = original.Scope with { SettingId = "other" } },
            original with { Scope = original.Scope with { ContentRevision = "other" } },
            original with { Payload = new BoundaryOccurrencePayload { BoundaryId = "boundary" } },
            original with { Kind = null! }, original with { Depth = 1 },
            original with { ParentEventId = original.EventId },
            original with { Tags = ImmutableArray<string>.Empty.Add("x").Add("x") },
            original with { Origin = new() { ContainerKind = "Card" } }
        }) Assert.True(invalid.Validate(Catalog()).IsFailure);
    }
}
