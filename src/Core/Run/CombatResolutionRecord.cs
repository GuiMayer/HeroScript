using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Flow;
using Core.Combat.Models;

namespace Core.Run;

public sealed record CombatAnimationFrame
{
    public Guid FrameId { get; init; }
    public int Index { get; init; }
    public int RunSequence { get; init; }
    public ulong CombatStep { get; init; }
    public string TransitionType { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public CombatState? StateAfter { get; init; }
    public int SnapshotSequence { get; init; }
}

public sealed record CombatResolutionRecord
{
    private ImmutableArray<CombatAnimationFrame> _frames = [];

    public Guid CommandId { get; init; }
    public Guid CombatId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public AnimationFrameMode Mode { get; init; }
    public int FirstSequence { get; init; }
    public int FinalSequence { get; init; }
    public IReadOnlyList<CombatAnimationFrame> Frames
    {
        get => _frames;
        init => _frames = value?.ToImmutableArray() ?? [];
    }
}
