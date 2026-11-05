using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;

namespace Core.Run.Content;

/// <summary>Copies descriptive facts from the pinned revision. No effects are executed.</summary>
public static class CardRuleReferenceProjector
{
    public static ImmutableArray<CardRuleReference> Read(ContentRuntime runtime, IEnumerable<CardEffectPresentation> effects)
    {
        var result = ImmutableArray.CreateBuilder<CardRuleReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
        foreach (var (kind, collection, id) in new[] {
            ("status", "status-effects", effect.Definition.StatusId),
            ("modifier", "modifiers", effect.Definition.ModifierId),
            ("recipe", "condensation-recipes", effect.Definition.CondensationRecipeId) })
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(kind + ":" + id)) continue;
            if (!runtime.GetDefinitions(collection).TryGetValue(id, out var definition)) continue;
            var influences = ImmutableArray.CreateBuilder<CardRuleInfluence>();
            var authored = Property(definition, "influences");
            if (authored.ValueKind == JsonValueKind.Array)
                foreach (var influence in authored.EnumerateArray())
                {
                    var value = Property(influence, "value");
                    influences.Add(new(Text(influence, "channel"),
                        string.IsNullOrWhiteSpace(Text(influence, "formula")) && value.TryGetSingleSafe(out var number) ? number : null));
                }
            result.Add(new(kind, id, Text(definition, "displayName"), Text(definition, "description"), influences.ToImmutable()));
        }
        return result.ToImmutable();
    }

    private static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        ? element.EnumerateObject().FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
    private static string Text(JsonElement element, string name) => Property(element, name) is var value && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty : string.Empty;
    private static bool TryGetSingleSafe(this JsonElement value, out float number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out number) && float.IsFinite(number);
    }
}
