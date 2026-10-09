using System.Collections.Immutable;
using System.Text.Json;
using Core.Effects;

namespace Core.Run;

/// <summary>
/// Immutable copy of the map content pinned when a run starts.
/// </summary>
public sealed record RunMapState
{
    private ImmutableList<RunMapNodeState> _nodes = [];
    private ImmutableList<string> _visitedNodeIds = [];
    private ImmutableList<string> _resolvedNodeIds = [];

    public IReadOnlyList<RunMapNodeState> Nodes
    {
        get => _nodes;
        init => _nodes = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<string> VisitedNodeIds
    {
        get => _visitedNodeIds;
        init => _visitedNodeIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<string> ResolvedNodeIds
    {
        get => _resolvedNodeIds;
        init => _resolvedNodeIds = value?.ToImmutableList() ?? [];
    }
}

public sealed record RunMapNodeState
{
    private ImmutableList<string> _nextNodeIds = [];
    private ImmutableDictionary<string, JsonElement> _metadata =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private ImmutableArray<EffectDefinition> _entryEffects = [];
    private ImmutableArray<EffectDefinition> _exitEffects = [];

    public string NodeId { get; init; } = string.Empty;
    public RunActivityEffectOwner EntryEffectOwner { get; init; }
    public RunActivityEffectOwner ExitEffectOwner { get; init; }
    public RunActivityDefinition Activity { get; init; } = new();
    public RunActivityCompletionPolicy CompletionPolicy { get; init; } = RunActivityCompletionPolicy.Required;
    public IReadOnlyList<EffectDefinition> EntryEffects
    {
        get => _entryEffects;
        init => _entryEffects = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectDefinition> ExitEffects
    {
        get => _exitEffects;
        init => _exitEffects = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<string> NextNodeIds
    {
        get => _nextNodeIds;
        init => _nextNodeIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyDictionary<string, JsonElement> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record RunAvailableCommand
{
    private ImmutableList<string> _targetNodeIds = [];

    public string Type { get; init; } = string.Empty;
    public string CurrentNodeId { get; init; } = string.Empty;
    public int ExpectedSequence { get; init; }
    public ulong ExpectedStep { get; init; }
    public JsonElement PayloadSchema { get; init; }
    public JsonElement ValidPayload { get; init; }

    public IReadOnlyList<string> TargetNodeIds
    {
        get => _targetNodeIds;
        init => _targetNodeIds = value?.ToImmutableList() ?? [];
    }
}

public static class RunCommandTypes
{
    public const string StartEncounter = "START_ENCOUNTER";
    public const string StartDialogue = "START_DIALOGUE";
    public const string ChooseDialogueOption = "CHOOSE_DIALOGUE_OPTION";
    public const string ResolveCombat = "RESOLVE_COMBAT";
    public const string ResolveNode = "RESOLVE_NODE";
    public const string AdvanceNode = "ADVANCE_NODE";
    public const string CreateCardSelection = "CREATE_CARD_SELECTION";
    public const string PickCardReward = "PICK_CARD_REWARD";
    public const string RerollCardReward = "REROLL_CARD_REWARD";
    public const string DecomposeCardReward = "DECOMPOSE_CARD_REWARD";
    public const string BuyShopItem = "BUY_SHOP_ITEM";
    public const string RerollShop = "REROLL_SHOP";
    public const string CreateShop = "CREATE_SHOP";
    public const string CreatePreparation = "CREATE_PREPARATION";
    public const string ApplyPreparationOption = "APPLY_PREPARATION_OPTION";
    public const string AcquireRelic = "ACQUIRE_RELIC";
    public const string RemoveRelic = "REMOVE_RELIC";
    public const string UpgradeCard = "UPGRADE_CARD";
    public const string RemoveCardTransformation = "REMOVE_CARD_TRANSFORMATION";
    public const string ReplaceCardTransformation = "REPLACE_CARD_TRANSFORMATION";
    public const string StartRun = "START_RUN";
    public const string CreateBranchFromHistory = "CREATE_BRANCH_FROM_HISTORY";
    public const string RestoreHeadFromHistory = "RESTORE_HEAD_FROM_HISTORY";
    public const string ActivateContentRevision = "ACTIVATE_CONTENT_REVISION";
    public const string ApplyRunResource = "APPLY_RUN_RESOURCE";
    public const string InvokeCardZoneFlow = "INVOKE_CARD_ZONE_FLOW";
    public const string InvokeCardZoneGameplayFlow = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW";
    public const string AbandonRun = "ABANDON_RUN";
}
