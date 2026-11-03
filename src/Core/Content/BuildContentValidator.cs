using System.Collections.Immutable;
using Core.Common;
using Core.Run.DeckLoadouts;
using Core.Run.Supports;

namespace Core.Content;

internal static class BuildContentValidator
{
    internal static void Validate(ContentRuntime runtime, ImmutableArray<string>.Builder errors)
    {
        Visit<DeckCompositionPolicyDefinition>("deck-composition-policies", value => value.Validate(runtime));
        Visit<DeckModuleDefinition>("deck-modules", value => value.Validate(runtime));
        Visit<SupportDefinition>("supports", value => value.Validate(runtime));
        Visit<SupportRoutineDefinition>("support-routines", value => value.Validate(runtime));
        Visit<SupportLoadoutPolicyDefinition>("support-loadout-policies", value => value.Validate(runtime));
        void Visit<T>(string kind, Func<T, Result> validate) where T : class
        {
            foreach (var id in runtime.GetDefinitions(kind).Keys.Order(StringComparer.Ordinal))
            {
                var value = runtime.GetDefinition<T>(kind, id);
                var result = value.IsFailure ? Result.Failure(value.Error) : validate(value.Value);
                if (result.IsFailure) errors.Add($"{kind}/{id}: {result.Error}");
            }
        }
    }
}
