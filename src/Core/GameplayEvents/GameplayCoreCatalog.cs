using System.Collections.Immutable;
using Core.Common;

namespace Core.GameplayEvents;

/// <summary>Trusted publisher capabilities are code, never capabilities supplied by a command.</summary>
public static class GameplayCoreCatalog
{
    public const string PublisherId = "core.gameplay";
    public static Result<GameplayEventCatalog> Create(string setting, string revision)
    {
        var kinds = new List<GameplayEventKindDefinition>();
        void Add(GameplayPayloadKind payload, params string[] ids) => kinds.AddRange(ids.Select(id => new GameplayEventKindDefinition
            { Kind = id, Payload = payload, PublisherId = PublisherId }));
        Add(GameplayPayloadKind.ResourceChange, "resource.change.resolved");
        Add(GameplayPayloadKind.AttributeChange, "attribute.change.resolved");
        Add(GameplayPayloadKind.StatusChange, "status.applied", "status.reapplied", "status.removed", "status.stacks.consumed", "status.expired", "status.duration.changed");
        Add(GameplayPayloadKind.ModifierChange, "modifier.stacks.changed");
        Add(GameplayPayloadKind.InstanceDuration, "modifier.duration.changed");
        Add(GameplayPayloadKind.CardMovement, "card.created", "card.moved", "card.destroyed");
        Add(GameplayPayloadKind.ZoneOrder, "zone.order.changed");
        Add(GameplayPayloadKind.ResourceThreshold, "resource.threshold.matched");
        Add(GameplayPayloadKind.ActorDefeatChange, "actor.defeat.changed");
        Add(GameplayPayloadKind.SequenceChange, "activation.sequence.changed");
        Add(GameplayPayloadKind.RuleResult, "effect.condensation.resolved");
        Add(GameplayPayloadKind.Boundary, "phase.entered", "phase.exited", "combat.started", "combat.ended", "activation.started", "activation.ended",
            "round.started", "round.ended", "run.started", "run.ended");
        Add(GameplayPayloadKind.Action, "action.started", "action.completed", "command.started", "command.completed");
        var contract = new GameplayPublisherContract(PublisherId, HierarchySelector.Compile(["**"]).Value, Enum.GetValues<GameplayPayloadKind>().ToImmutableArray());
        return GameplayEventCatalog.Create(setting, revision, [contract], kinds, [new() { ReasonId = "effect.application" },
            new() { ReasonId = "cost.payment" }, new() { ReasonId = "cost.refund" }, new() { ReasonId = "command.input" }]);
    }
}
