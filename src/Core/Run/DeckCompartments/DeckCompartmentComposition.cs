using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run.Content;

namespace Core.Run.DeckCompartments;

/// <summary>Adapter from build associations to ordinary structural component contributions.</summary>
public static class DeckCompartmentComposition
{
    public static Result<EffectiveCardDefinition> Resolve(RunState run, ContentRuntime runtime,
        CompiledCardDefinition definition, CardInstanceState instance, IEffectiveCardResolver resolver, ICardContentCompiler? compiler = null)
    {
        // No new resolution, hashing or profile lookup in the historical, disabled path.
        if (run.DeckCompartments == null) return resolver.Resolve(definition, instance);
        var owners = run.DeckCompartments.Actors.Values.Where(a => a.Allocations.ContainsKey(instance.CardInstanceId)).ToArray();
        if (owners.Length == 0) return resolver.Resolve(definition, instance);
        if (owners.Length != 1) return Result<EffectiveCardDefinition>.Failure("Card is allocated to multiple characters");
        var actor = owners[0];
        if (!DeckCompartmentAccess.Owns(run, actor.ActorId, instance))
            return Result<EffectiveCardDefinition>.Failure("Compartment allocation ownership is invalid");
        var profile = DeckCompartmentAccess.Profile(run, runtime, actor.ActorId, compiler);
        if (profile.IsFailure) return Result<EffectiveCardDefinition>.Failure(profile.Error);
        if (profile.Value.ProfileId != actor.ProfileId)
            return Result<EffectiveCardDefinition>.Failure("Allocated profile no longer matches the character");
        var compartment = profile.Value.Compartments.SingleOrDefault(c => c.CompartmentId == actor.Allocations[instance.CardInstanceId]);
        if (compartment == null) return Result<EffectiveCardDefinition>.Failure("Allocated compartment no longer exists");
        if (!compartment.RequiredTags.IsEmpty || !compartment.ExcludedTags.IsEmpty)
        {
            var own = resolver.Resolve(definition, instance);
            if (own.IsFailure) return own;
            if (!compartment.RequiredTags.All(own.Value.Tags.Contains) || compartment.ExcludedTags.Any(own.Value.Tags.Contains))
                return Result<EffectiveCardDefinition>.Failure("Card is incompatible with compartment requirements");
        }
        var sources = ImmutableArray.CreateBuilder<CardComponentContribution>();
        foreach (var grant in compartment.Grants.OrderBy(g => g.Priority).ThenBy(g => g.GrantId, StringComparer.Ordinal))
        {
            // Length-prefixed canonical source identity; no delimiter ambiguity or random ID allocation.
            var source = CanonicalJson.Serialize(new { actorId = actor.ActorId, profileId = actor.ProfileId,
                compartmentId = compartment.CompartmentId, grantId = grant.GrantId });
            var scope = "grant_" + CanonicalJson.ComputeHash(source)[..32];
            var bundle = runtime.GetDefinition<CardComponentBundleDefinition>("card-component-bundles", grant.BundleId);
            if (bundle.IsFailure) return Result<EffectiveCardDefinition>.Failure(bundle.Error);
            var expanded = CardBundleCompiler.Expand(bundle.Value, scope, grant.EffectBindings, compiler);
            if (expanded.IsFailure) return Result<EffectiveCardDefinition>.Failure(expanded.Error);
            sources.Add(new() { SourceId = source, ContentRevision = runtime.Manifest.Revision, Priority = grant.Priority,
                Components = expanded.Value, Tags = compartment.GrantedTags, NameKey = compartment.NameKey, DescriptionKey = compartment.DescriptionKey });
        }
        return resolver.ResolveWithContributions(definition, instance, sources.ToImmutable());
    }

    public static Result Validate(RunState run, ContentRuntime runtime)
    {
        if (run.DeckCompartments == null) return Result.Success();
        foreach (var (key, actor) in run.DeckCompartments.Actors)
        {
            if (key != actor.ActorId) return Result.Failure("Compartment actor key mismatch");
            var profile = DeckCompartmentAccess.Profile(run, runtime, actor.ActorId);
            if (profile.IsFailure) return Result.Failure(profile.Error);
            if (actor.ProfileId != profile.Value.ProfileId) return Result.Failure("Allocated profile mismatch");
            foreach (var group in actor.Allocations.GroupBy(a => a.Value, StringComparer.Ordinal))
            {
                var compartment = profile.Value.Compartments.SingleOrDefault(c => c.CompartmentId == group.Key);
                if (compartment == null || group.Count() > compartment.Capacity) return Result.Failure("Invalid allocated compartment capacity");
            }
            foreach (var id in actor.Allocations.Keys)
            {
                var card = run.Deck.GetCard(id);
                if (card == null) return Result.Failure("Allocated card does not exist");
                var compiled = new CardContentCompiler().Compile(card.DefinitionId, runtime);
                if (compiled.IsFailure) return Result.Failure(compiled.Error);
                var effective = Resolve(run, runtime, compiled.Value, card, new EffectiveCardResolver());
                if (effective.IsFailure) return Result.Failure(effective.Error);
                var executable = GameplayContentValidator.ValidateCardContainer(runtime, $"compartment-card/{id}", effective.Value.Components);
                if (executable.IsFailure) return executable;
            }
        }
        return Result.Success();
    }
}
