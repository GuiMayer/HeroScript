using System.Collections.Immutable;
using Core.Combat.TurnPhase;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content;

/// <summary>Schema 2 has one authoring authority. Schema 1 remains readable for captured historical bundles.</summary>
internal static class CanonicalGameplayAuthoring
{
    internal static ImmutableArray<string> Validate(ContentRuntime runtime)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        foreach (var id in runtime.GetDefinitions("status-effects").Keys)
        {
            var item = runtime.GetDefinition<StatusEffectDefinition>("status-effects", id);
            if (item.IsSuccess && item.Value.Triggers.Count > 0)
                errors.Add($"status-effects/{id}: historical triggers require migration to responses for content schema 2");
        }
        foreach (var id in runtime.GetDefinitions("relics").Keys)
        {
            var item = runtime.GetDefinition<RelicDefinition>("relics", id);
            if (item.IsSuccess && (item.Value.Triggers.Count > 0 || item.Value.Reactions is { IsEmpty: false }))
                errors.Add($"relics/{id}: historical triggers/reactions require migration to responses for content schema 2");
        }
        foreach (var id in runtime.GetDefinitions("phase-sequences").Keys)
        {
            var item = runtime.GetDefinition<PhaseSequenceDefinition>("phase-sequences", id);
            if (item.IsSuccess && item.Value.Phases.Any(phase => phase.EntryEffects.Count > 0 || phase.ExitEffects.Count > 0))
                errors.Add($"phase-sequences/{id}: historical entry/exit effects require migration to responses for content schema 2");
        }
        return errors.ToImmutable();
    }
}
