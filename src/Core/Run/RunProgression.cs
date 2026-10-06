using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;

namespace Core.Run;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunLifecycleState
{
    Active,
    Completed,
    Failed,
    Abandoned
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunProgressionTransition
{
    Continue,
    Complete,
    Fail,
    Abandon
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunEncounterRetryPolicy
{
    Disabled,
    RestartActivity
}

public sealed record RunProgressionPolicyDefinition
{
    private ImmutableArray<CombatStatus> _retryableEncounterOutcomes = [];

    public string ProgressionPolicyId { get; init; } = string.Empty;
    public RunProgressionTransition EncounterVictory { get; init; } = RunProgressionTransition.Continue;
    public RunProgressionTransition EncounterDefeat { get; init; } = RunProgressionTransition.Fail;
    public RunProgressionTransition EncounterDraw { get; init; } = RunProgressionTransition.Fail;
    public RunProgressionTransition EncounterAbandoned { get; init; } = RunProgressionTransition.Fail;
    public RunProgressionTransition EndOfMap { get; init; } = RunProgressionTransition.Complete;
    public RunEncounterRetryPolicy EncounterRetry { get; init; } = RunEncounterRetryPolicy.Disabled;
    public IReadOnlyList<CombatStatus> RetryableEncounterOutcomes
    {
        get => _retryableEncounterOutcomes;
        init => _retryableEncounterOutcomes = value?.Distinct().Order().ToImmutableArray() ?? [];
    }
    public bool AllowAbandon { get; init; }
    public bool AllowOutOfActivityCommands { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunActivityType
{
    Encounter,
    RelicReward,
    CardSelection,
    Shop,
    Preparation,
    CardUpgrade,
    Dialogue
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunActivityCompletionPolicy
{
    Required,
    Optional
}

/// <summary>A typed activity captured in the immutable map snapshot.</summary>
public sealed record RunActivityDefinition
{
    private ImmutableDictionary<string, JsonElement> _parameters =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public RunActivityType Type { get; init; }
    public string? DefinitionId { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Parameters
    {
        get => _parameters;
        init => _parameters = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public interface IRunActivityHandler
{
    RunActivityType Type { get; }
    string? DefinitionKind { get; }
    IReadOnlySet<string> CommandTypes { get; }
    Result Validate(RunActivityDefinition activity);
    Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload);
    bool IsComplete(RunState run, RunMapNodeState node);
    IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node);
}

public sealed class RunActivityRegistry
{
    private readonly ImmutableDictionary<RunActivityType, IRunActivityHandler> _handlers;

    public RunActivityRegistry(IEnumerable<IRunActivityHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var materialized = handlers.OrderBy(handler => handler.Type).ToArray();
        var duplicate = materialized.GroupBy(handler => handler.Type).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate run activity handler: {duplicate.Key}");
        _handlers = materialized.ToImmutableDictionary(handler => handler.Type);
    }

    public Result<IRunActivityHandler> Resolve(RunActivityType type) =>
        _handlers.TryGetValue(type, out var handler)
            ? Result<IRunActivityHandler>.Success(handler)
            : Result<IRunActivityHandler>.Failure($"Run activity type is not registered: {type}");

    public Result Validate(RunActivityDefinition activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var handler = Resolve(activity.Type);
        return handler.IsFailure ? Result.Failure(handler.Error) : handler.Value.Validate(activity);
    }

    public bool IsActivityCommand(string commandType) => _handlers.Values.Any(handler =>
        handler.CommandTypes.Contains(commandType));

    public static RunActivityRegistry CreateDefault() => new(
    [
        new EncounterRunActivityHandler(),
        new RelicRewardRunActivityHandler(),
        new CardSelectionRunActivityHandler(),
        new ShopRunActivityHandler(),
        new PreparationRunActivityHandler(),
        new CardUpgradeRunActivityHandler(),
        new Dialogue.DialogueRunActivityHandler()
    ]);
}

public sealed class RunProgressionService(RunActivityRegistry activities)
{
    public Result ValidateCommand(RunState run, string commandType, object payload)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentNullException.ThrowIfNull(payload);
        if (!activities.IsActivityCommand(commandType))
            return Result.Success();
        if (run.ResolvedMode?.ProgressionPolicy.AllowOutOfActivityCommands == true)
            return Result.Success();
        if (run.Lifecycle != RunLifecycleState.Active || run.CurrentNodeId == null)
            return Result.Failure($"Run is not active: {run.Lifecycle}");
        var current = FindCurrent(run);
        if (current == null)
            return Result.Failure($"Map node not found: {run.CurrentNodeId}");
        var handler = activities.Resolve(current.Activity.Type);
        if (handler.IsFailure)
            return Result.Failure(handler.Error);
        if (!handler.Value.CommandTypes.Contains(commandType))
            return Result.Failure($"Command {commandType} is not legal for activity {current.Activity.Type}");
        if (!handler.Value.GetAvailableCommands(run, current).Any(command => command.Type == commandType))
            return Result.Failure($"Command {commandType} is not currently available for node {current.NodeId}");
        return handler.Value.ValidateCommand(run, current, commandType, payload);
    }

    public Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Lifecycle != RunLifecycleState.Active || run.CurrentNodeId == null)
            return Result<IReadOnlyList<RunAvailableCommand>>.Success([]);

        var current = FindCurrent(run);
        if (current == null)
            return Result<IReadOnlyList<RunAvailableCommand>>.Failure($"Map node not found: {run.CurrentNodeId}");

        if (run.Map.ResolvedNodeIds.Contains(current.NodeId, StringComparer.Ordinal))
        {
            var targets = current.NextNodeIds
                .Where(nodeId => !run.Map.VisitedNodeIds.Contains(nodeId, StringComparer.Ordinal))
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .ToArray();
            IReadOnlyList<RunAvailableCommand> commands = targets.Length == 0
                ? []
                : [Command(run, current, RunCommandTypes.AdvanceNode, new { targetNodeId = "string" }, new { targetNodeIds = targets }) with
                {
                    TargetNodeIds = targets
                }];
            return Result<IReadOnlyList<RunAvailableCommand>>.Success(WithAbandon(run, current, commands));
        }

        var handler = activities.Resolve(current.Activity.Type);
        if (handler.IsFailure)
            return Result<IReadOnlyList<RunAvailableCommand>>.Failure(handler.Error);
        return Result<IReadOnlyList<RunAvailableCommand>>.Success(
            WithAbandon(run, current, handler.Value.GetAvailableCommands(run, current)));
    }

    public Result CanResolve(RunState run, RunMapNodeState node)
    {
        var handler = activities.Resolve(node.Activity.Type);
        if (handler.IsFailure)
            return Result.Failure(handler.Error);
        return node.CompletionPolicy == RunActivityCompletionPolicy.Optional || handler.Value.IsComplete(run, node)
            ? Result.Success()
            : Result.Failure($"Run activity is not complete: {node.NodeId}");
    }

    public RunState ApplyNodeExit(RunState run, RunMapNodeState node)
    {
        var next = Core.Combat.Modifiers.ModifierTransitions.Tick(
            run,
            Core.Combat.Modifiers.ModifierDurationBoundary.Node);
        if (node.NextNodeIds.Count != 0)
            return next;

        next = Core.Combat.Modifiers.ModifierTransitions.Tick(
            next,
            Core.Combat.Modifiers.ModifierDurationBoundary.Run);
        return ApplyTransition(next, next.ResolvedMode?.ProgressionPolicy.EndOfMap
            ?? RunProgressionTransition.Complete);
    }

    public RunState ApplyEncounterOutcome(RunState run, CombatStatus outcome)
    {
        var policy = run.ResolvedMode?.ProgressionPolicy ?? new RunProgressionPolicyDefinition();
        var transition = outcome switch
        {
            CombatStatus.VICTORY => policy.EncounterVictory,
            CombatStatus.DEFEAT => policy.EncounterDefeat,
            CombatStatus.DRAW => policy.EncounterDraw,
            CombatStatus.ABANDONED => policy.EncounterAbandoned,
            _ => RunProgressionTransition.Continue
        };
        return ApplyTransition(run, transition);
    }

    public bool ShouldRestartEncounterActivity(RunState run, CombatStatus outcome)
    {
        var policy = run.ResolvedMode?.ProgressionPolicy;
        return policy?.EncounterRetry == RunEncounterRetryPolicy.RestartActivity &&
            policy.RetryableEncounterOutcomes.Contains(outcome);
    }

    public Result<RunState> Abandon(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Lifecycle != RunLifecycleState.Active)
            return Result<RunState>.Failure($"Run is not active: {run.Lifecycle}");
        if (run.ResolvedMode?.ProgressionPolicy.AllowAbandon != true)
            return Result<RunState>.Failure("Game mode does not allow abandoning a run");
        return Result<RunState>.Success(run with
        {
            Lifecycle = RunLifecycleState.Abandoned,
            Determinism = run.Determinism.AdvanceStep()
        });
    }

    internal static RunAvailableCommand Command(
        RunState run,
        RunMapNodeState node,
        string type,
        object schema,
        object? validPayload = null) => new()
    {
        Type = type,
        CurrentNodeId = node.NodeId,
        ExpectedSequence = run.Sequence,
        ExpectedStep = run.Determinism.Step,
        PayloadSchema = JsonSerializer.SerializeToElement(schema),
        ValidPayload = JsonSerializer.SerializeToElement(validPayload ?? new { })
    };

    private static RunMapNodeState? FindCurrent(RunState run) => run.Map.Nodes.FirstOrDefault(node =>
        string.Equals(node.NodeId, run.CurrentNodeId, StringComparison.Ordinal));

    private static IReadOnlyList<RunAvailableCommand> WithAbandon(
        RunState run,
        RunMapNodeState node,
        IReadOnlyList<RunAvailableCommand> commands) =>
        run.ResolvedMode?.ProgressionPolicy.AllowAbandon == true
            ? commands.Append(Command(run, node, RunCommandTypes.AbandonRun, new { })).ToArray()
            : commands;

    private static RunState ApplyTransition(RunState run, RunProgressionTransition transition) => transition switch
    {
        RunProgressionTransition.Continue => run,
        RunProgressionTransition.Complete => run with { Lifecycle = RunLifecycleState.Completed },
        RunProgressionTransition.Fail => run with { Lifecycle = RunLifecycleState.Failed },
        RunProgressionTransition.Abandon => run with { Lifecycle = RunLifecycleState.Abandoned },
        _ => throw new InvalidOperationException($"Unsupported run progression transition: {transition}")
    };
}

internal abstract class RunActivityHandlerBase : IRunActivityHandler
{
    public abstract RunActivityType Type { get; }
    public abstract string? DefinitionKind { get; }
    public abstract IReadOnlySet<string> CommandTypes { get; }
    protected virtual bool RequiresDefinition => true;

    public virtual Result Validate(RunActivityDefinition activity)
    {
        if (activity.Type != Type)
            return Result.Failure($"Activity handler {Type} cannot validate {activity.Type}");
        return RequiresDefinition && string.IsNullOrWhiteSpace(activity.DefinitionId)
            ? Result.Failure($"Run activity {Type} requires definitionId")
            : Result.Success();
    }

    public abstract bool IsComplete(RunState run, RunMapNodeState node);
    public abstract IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node);
    public virtual Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload) =>
        CommandTypes.Contains(commandType)
            ? Result.Success()
            : Result.Failure($"Command {commandType} is not owned by activity {Type}");

    protected static RunAvailableCommand Command(
        RunState run,
        RunMapNodeState node,
        string type,
        object schema,
        object? validPayload = null) => RunProgressionService.Command(run, node, type, schema, validPayload);

    protected static RunAvailableCommand Resolve(RunState run, RunMapNodeState node) =>
        Command(run, node, RunCommandTypes.ResolveNode, new { currentNodeId = "string" }, new { currentNodeId = node.NodeId });
}

internal sealed class EncounterRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.Encounter;
    public override string? DefinitionKind => null;
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.StartEncounter,
        RunCommandTypes.ResolveCombat
    };
    protected override bool RequiresDefinition => false;

    public override bool IsComplete(RunState run, RunMapNodeState node) => run.Encounters.Any(encounter =>
        encounter.NodeId == node.NodeId && !encounter.Combat.IsActive);

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        var encounter = run.ActiveEncounterId is { } id ? run.GetEncounter(id) : null;
        if (encounter == null)
        {
            var payload = new Dictionary<string, object>();
            if (node.Activity.Parameters.TryGetValue("participants", out var participants))
                payload["participants"] = participants.Clone();
            if (node.Activity.Parameters.TryGetValue("initialResourceValues", out var resources))
                payload["initialResourceValues"] = resources.Clone();

            return
            [
                Command(
                    run,
                    node,
                    RunCommandTypes.StartEncounter,
                    new { participants = "array", initialResourceValues = "object?" },
                    payload)
            ];
        }
        if (encounter.Combat.IsActive)
            return [];
        var resolve = Command(
            run,
            node,
            RunCommandTypes.ResolveCombat,
            new { combatId = "guid" },
            new { combatId = encounter.Combat.CombatId });
        return [resolve with { ExpectedStep = encounter.Combat.Determinism.Step }];
    }
}

internal sealed class RelicRewardRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.RelicReward;
    public override string? DefinitionKind => "relics";
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.AcquireRelic,
        RunCommandTypes.ResolveNode
    };

    public override Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload) =>
        payload is RelicCommand acquire && acquire.RelicId != node.Activity.DefinitionId
            ? Result.Failure("Relic command does not match the current activity definition")
            : base.ValidateCommand(run, node, commandType, payload);

    public override bool IsComplete(RunState run, RunMapNodeState node) =>
        run.CompletedActivityNodeIds.Contains(node.NodeId, StringComparer.Ordinal);

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node) =>
        IsComplete(run, node)
            ? [Resolve(run, node)]
            : [Command(run, node, RunCommandTypes.AcquireRelic,
                new { relicId = "string" }, new { relicId = node.Activity.DefinitionId })];
}

internal sealed class CardSelectionRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.CardSelection;
    public override string? DefinitionKind => "card-selections";
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.CreateCardSelection,
        RunCommandTypes.PickCardReward,
        RunCommandTypes.RerollCardReward,
        RunCommandTypes.DecomposeCardReward,
        RunCommandTypes.ResolveNode
    };

    public override Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload)
    {
        var selection = Selection(run, node);
        return payload switch
        {
            CardSelectionCommand create when create.SelectionId != node.Activity.DefinitionId =>
                Result.Failure("Card selection command does not match the current activity definition"),
            CardSelectionCardsCommand pick when selection?.SelectionInstanceId != pick.SelectionInstanceId =>
                Result.Failure("Card selection instance does not belong to the current activity"),
            RerollCardSelectionCommand reroll when selection?.SelectionInstanceId != reroll.SelectionInstanceId =>
                Result.Failure("Card selection instance does not belong to the current activity"),
            CardSelectionItemCommand decompose when selection?.SelectionInstanceId != decompose.SelectionInstanceId =>
                Result.Failure("Card selection instance does not belong to the current activity"),
            _ => base.ValidateCommand(run, node, commandType, payload)
        };
    }

    public override bool IsComplete(RunState run, RunMapNodeState node) => Selection(run, node)?.Completed == true;

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        var selection = Selection(run, node);
        if (selection == null)
        {
            var commands = new List<RunAvailableCommand>
            {
                Command(run, node, RunCommandTypes.CreateCardSelection,
                    new { selectionId = "string" }, new { selectionId = node.Activity.DefinitionId })
            };
            if (node.CompletionPolicy == RunActivityCompletionPolicy.Optional) commands.Add(Resolve(run, node));
            return commands;
        }
        if (selection.Completed)
            return [Resolve(run, node)];
        var options = selection.Options.Where(option => !option.Decomposed).Select(option => option.CardId).Order().ToArray();
        var available = new List<RunAvailableCommand>
        {
            Command(run, node, RunCommandTypes.PickCardReward,
                new { selectionInstanceId = "guid", cardIds = "array" },
                new { selectionInstanceId = selection.SelectionInstanceId, cardIds = options, pickCount = selection.PickCount })
        };
        available.Add(
            Command(run, node, RunCommandTypes.RerollCardReward,
                new { selectionInstanceId = "guid", lockedCardIds = "array?" },
                new { selectionInstanceId = selection.SelectionInstanceId, lockableCardIds = options }));
        if (selection.Decompose.Enabled)
        {
            available.Add(Command(run, node, RunCommandTypes.DecomposeCardReward,
                new { selectionInstanceId = "guid", cardId = "string" },
                new { selectionInstanceId = selection.SelectionInstanceId, cardIds = options }));
        }
        return available;
    }

    private static CardSelectionState? Selection(RunState run, RunMapNodeState node) => run.CardSelections
        .LastOrDefault(selection => selection.NodeId == node.NodeId);
}

internal sealed class ShopRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.Shop;
    public override string? DefinitionKind => "shops";
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.CreateShop,
        RunCommandTypes.BuyShopItem,
        RunCommandTypes.RerollShop,
        RunCommandTypes.ResolveNode
    };

    public override Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload)
    {
        var shop = Shop(run, node);
        return payload switch
        {
            ShopDefinitionCommand create when create.ShopId != node.Activity.DefinitionId =>
                Result.Failure("Shop command does not match the current activity definition"),
            ShopItemCommand buy when shop?.ShopInstanceId != buy.ShopInstanceId =>
                Result.Failure("Shop instance does not belong to the current activity"),
            ShopCommand reroll when shop?.ShopInstanceId != reroll.ShopInstanceId =>
                Result.Failure("Shop instance does not belong to the current activity"),
            _ => base.ValidateCommand(run, node, commandType, payload)
        };
    }
    public override bool IsComplete(RunState run, RunMapNodeState node) => Shop(run, node) != null;

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        var shop = Shop(run, node);
        if (shop == null)
        {
            var commands = new List<RunAvailableCommand>
            {
                Command(run, node, RunCommandTypes.CreateShop,
                    new { shopId = "string" }, new { shopId = node.Activity.DefinitionId })
            };
            if (node.CompletionPolicy == RunActivityCompletionPolicy.Optional) commands.Add(Resolve(run, node));
            return commands;
        }
        var items = shop.Items.Where(item => !item.Purchased).Select(item => item.ItemId).Order().ToArray();
        return
        [
            Command(run, node, RunCommandTypes.BuyShopItem,
                new { shopInstanceId = "guid", itemId = "string" },
                new { shopInstanceId = shop.ShopInstanceId, itemIds = items }),
            Command(run, node, RunCommandTypes.RerollShop,
                new { shopInstanceId = "guid" }, new { shopInstanceId = shop.ShopInstanceId }),
            Resolve(run, node)
        ];
    }

    private static ShopState? Shop(RunState run, RunMapNodeState node) => run.Shops
        .LastOrDefault(shop => shop.NodeId == node.NodeId);
}

internal sealed class PreparationRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.Preparation;
    public override string? DefinitionKind => "preparations";
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.CreatePreparation,
        RunCommandTypes.ApplyPreparationOption,
        RunCommandTypes.ResolveNode
    };

    public override Result ValidateCommand(RunState run, RunMapNodeState node, string commandType, object payload)
    {
        var preparation = Preparation(run, node);
        return payload switch
        {
            PreparationDefinitionCommand create when create.PreparationId != node.Activity.DefinitionId =>
                Result.Failure("Preparation command does not match the current activity definition"),
            PreparationCommand apply when preparation?.PreparationInstanceId != apply.PreparationInstanceId =>
                Result.Failure("Preparation instance does not belong to the current activity"),
            _ => base.ValidateCommand(run, node, commandType, payload)
        };
    }
    public override bool IsComplete(RunState run, RunMapNodeState node) => Preparation(run, node)?.AppliedOptionIds.Count > 0;

    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        var preparation = Preparation(run, node);
        if (preparation == null)
        {
            var initialCommands = new List<RunAvailableCommand>
            {
                Command(run, node, RunCommandTypes.CreatePreparation,
                    new { preparationId = "string" }, new { preparationId = node.Activity.DefinitionId })
            };
            if (node.CompletionPolicy == RunActivityCompletionPolicy.Optional) initialCommands.Add(Resolve(run, node));
            return initialCommands;
        }
        var options = preparation.Options.Where(option => !option.Applied).Select(option => option.OptionId).Order().ToArray();
        var commands = new List<RunAvailableCommand>
        {
            Command(run, node, RunCommandTypes.ApplyPreparationOption,
                new { preparationInstanceId = "guid", optionId = "string" },
                new { preparationInstanceId = preparation.PreparationInstanceId, optionIds = options })
        };
        if (IsComplete(run, node)) commands.Add(Resolve(run, node));
        return commands;
    }

    private static PreparationState? Preparation(RunState run, RunMapNodeState node) => run.Preparations
        .LastOrDefault(preparation => preparation.NodeId == node.NodeId);
}

internal sealed class CardUpgradeRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.CardUpgrade;
    public override string? DefinitionKind => null;
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RunCommandTypes.UpgradeCard,
        RunCommandTypes.ResolveNode
    };
    protected override bool RequiresDefinition => false;
    public override bool IsComplete(RunState run, RunMapNodeState node) =>
        run.CompletedActivityNodeIds.Contains(node.NodeId, StringComparer.Ordinal);
    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node)
    {
        if (IsComplete(run, node)) return [Resolve(run, node)];
        var commands = new List<RunAvailableCommand>
        {
            Command(run, node, RunCommandTypes.UpgradeCard,
                new { cardInstanceId = "guid", upgradeId = "string" },
                new
                {
                    cardInstanceIds = run.Deck.Topology.Instances.Keys.Order().ToArray(),
                    upgradeIds = node.Activity.Parameters.TryGetValue("upgradeIds", out var upgradeIds)
                        ? upgradeIds
                        : JsonSerializer.SerializeToElement(Array.Empty<string>())
                })
        };
        if (node.CompletionPolicy == RunActivityCompletionPolicy.Optional) commands.Add(Resolve(run, node));
        return commands;
    }
}

public sealed record CardUpgradeCommandOption(
    Guid CardInstanceId,
    string CardDefinitionId,
    string UpgradeId);

/// <summary>
/// Projects only executable card/upgrade pairs. This keeps discovery and
/// command execution aligned instead of asking clients to infer content rules.
/// </summary>
public static class CardUpgradeCommandOptions
{
    public static IReadOnlyList<CardUpgradeCommandOption> Project(
        RunState run,
        IEnumerable<CardUpgradeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definitions);
        var upgrades = definitions
            .OrderBy(definition => definition.UpgradeId, StringComparer.Ordinal)
            .ToArray();
        return run.Deck.Topology.Instances.Values
            .OrderBy(card => card.CreationOrdinal)
            .ThenBy(card => card.CardInstanceId)
            .SelectMany(card => upgrades
                .Where(upgrade => upgrade.AppliesTo(card.DefinitionId))
                .Where(upgrade => CardTransformationLedger.Count(card, upgrade.UpgradeId) < upgrade.MaxApplications)
                .Select(upgrade => new CardUpgradeCommandOption(
                    card.CardInstanceId,
                    card.DefinitionId,
                    upgrade.UpgradeId)))
            .ToArray();
    }
}
