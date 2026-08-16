using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;

namespace Core.Run;

public sealed record RunState
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public string ConfigName { get; init; } = "default";
    public string PlayerEntityId { get; init; } = "player";
    public int Gold { get; init; }
    public int PowerPoints { get; init; }
    public string? CurrentNodeId { get; init; }
    public DeckState Deck { get; init; } = new();
    public ImmutableArray<CardSelectionState> CardSelections { get; init; } = [];
    public ImmutableArray<ShopState> Shops { get; init; } = [];
    public ImmutableArray<PreparationState> Preparations { get; init; } = [];
    public ImmutableDictionary<string, JsonElement> Metadata { get; init; } =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    public ContentManifest? ContentManifest { get; init; }
    public DeterministicContext Determinism { get; init; } =
        DeterministicContext.Create(0, "legacy");
}
