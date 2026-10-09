using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Effects;
using Core.Resources;

namespace Core.Run.Dialogue;

/// <summary>Localized presentation data. English is the required fallback, never a gameplay input.</summary>
public sealed record DialogueText
{
    public string Text { get; init; } = string.Empty;
    public ImmutableDictionary<string, string> Translations { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public sealed record DialogueDefinition
{
    public string DialogueId { get; init; } = string.Empty;
    public DialogueText Title { get; init; } = new();
    public string StartNodeId { get; init; } = string.Empty;
    public ImmutableArray<DialogueNodeDefinition> Nodes { get; init; } = [];
}

public sealed record DialogueNodeDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public DialogueText Speaker { get; init; } = new();
    public DialogueText Text { get; init; } = new();
    public string? PortraitId { get; init; }
    public ImmutableArray<EffectDefinition> EntryEffects { get; init; } = [];
    public RunActivityEffectOwner EntryEffectOwner { get; init; }
    public ImmutableArray<DialogueChoiceDefinition> Choices { get; init; } = [];
}

public sealed record DialogueChoiceDefinition
{
    public string ChoiceId { get; init; } = string.Empty;
    public DialogueText Text { get; init; } = new();
    /// <summary>Null closes the conversation; a single choice also represents a linear Continue.</summary>
    public string? NextNodeId { get; init; }
    public DialogueCondition? Condition { get; init; }
    public bool HideWhenUnavailable { get; init; }
    public bool Once { get; init; }
    public DialogueText UnavailableText { get; init; } = new() { Text = "Requirements not met." };
    public ImmutableArray<ResourceAmount> Costs { get; init; } = [];
    public ImmutableArray<EffectDefinition> Effects { get; init; } = [];
    public RunActivityEffectOwner EffectOwner { get; init; }
    public ImmutableDictionary<string, string> SetFlags { get; init; } = ImmutableDictionary<string, string>.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DialogueConditionKind { All, Any, Not, Flag, ResourceAtLeast, HasCard, HasRelic, VisitedNode, ResolvedNode }

public sealed record DialogueCondition
{
    public DialogueConditionKind Kind { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Value { get; init; } = "true";
    public float Amount { get; init; }
    public ImmutableArray<DialogueCondition> Children { get; init; } = [];
}

public sealed record DialogueVisit(string NodeId, string? ChoiceId);

public sealed record RunNarrativeState
{
    public ImmutableArray<DialogueState> Dialogues { get; init; } = [];
    public ImmutableDictionary<string, string> Flags { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public sealed record DialogueState
{
    public Guid DialogueInstanceId { get; init; }
    public string ActivityNodeId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public DialogueDefinition Definition { get; init; } = new();
    public string CurrentNodeId { get; init; } = string.Empty;
    public bool Completed { get; init; }
    public ImmutableArray<DialogueVisit> History { get; init; } = [];
}

public sealed record StartDialogueCommand(string DialogueId);
public sealed record ChooseDialogueOptionCommand(Guid DialogueInstanceId, string NodeId, string ChoiceId);
public sealed record DialogueChoiceView(string ChoiceId, DialogueText Text, bool Available,
    DialogueText? UnavailableText, ImmutableArray<ResourceAmount> Costs);
public sealed record DialogueTranscriptLine(string NodeId, string? ChoiceId, DialogueText Speaker, DialogueText Text);
public sealed record DialogueView(Guid DialogueInstanceId, string DialogueId, string ActivityNodeId,
    string ContentRevision, DialogueText Title, string NodeId, DialogueText Speaker, DialogueText Text,
    string? PortraitId, bool Completed, ImmutableArray<DialogueChoiceView> Choices,
    ImmutableArray<DialogueVisit> History, ImmutableArray<DialogueTranscriptLine> Transcript);
