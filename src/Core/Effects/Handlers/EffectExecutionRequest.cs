using Core.Damage;

namespace Core.Effects.Handlers;

public sealed record EffectExecutionRequest(
    EffectInstance Effect,
    string TargetId,
    IEffectContext Context,
    IRandomProvider RandomProvider,
    bool UseExplicitDamageRandom);
