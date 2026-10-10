using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Resources;

namespace Core.Run.Projections;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterResourceAuthority { Unavailable, PersistentPlayer, ActiveEncounter }
public sealed record ResourcePoolView(float Current, float Minimum, float Maximum);
public sealed record CharacterResourceView(string OwnerId, CharacterResourceAuthority Authority, Guid? CombatId,
    ImmutableSortedDictionary<string, ResourcePoolView> Resources);

/// <summary>Never combines a combat actor with its persistent baseline or the run wallet.</summary>
public static class RunResourceProjection
{
    public static CharacterResourceView Character(RunState run)
    {
        var encounter = run.GetActiveEncounter();
        if (encounter != null)
        {
            var actor = encounter.Combat.GetAllActors().FirstOrDefault(item => item.InstanceId == run.PlayerEntityId);
            return new(run.PlayerEntityId, actor == null ? CharacterResourceAuthority.Unavailable : CharacterResourceAuthority.ActiveEncounter,
                encounter.Combat.CombatId, Project(actor?.ResourceState));
        }
        var resource = run.PlayerEntity?.Components.Values.OfType<ResourceEntityComponentState>().SingleOrDefault()?.State;
        return new(run.PlayerEntityId, resource == null ? CharacterResourceAuthority.Unavailable : CharacterResourceAuthority.PersistentPlayer,
            null, Project(resource));
    }

    private static ImmutableSortedDictionary<string, ResourcePoolView> Project(ResourceSet? resources) =>
        (resources?.Resources ?? new Dictionary<string, ResourcePool>()).ToImmutableSortedDictionary(pair => pair.Key,
            pair => new ResourcePoolView(pair.Value.Current, pair.Value.Minimum, pair.Value.Maximum), StringComparer.Ordinal);
}
