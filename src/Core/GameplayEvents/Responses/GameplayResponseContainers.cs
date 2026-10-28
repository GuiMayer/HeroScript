using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;
using Core.Entity.Definitions;
using Core.Run;
using Core.Run.Content;

namespace Core.GameplayEvents.Responses;

public interface IGameplayResponseDeclarations
{
    ImmutableArray<GameplayResponseDefinition>? Responses { get; }
}

public sealed record EntityResponseComponentDefinition : EntityComponentDefinition, IGameplayResponseDeclarations
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<GameplayResponseDefinition>? Responses { get; init; }
}

public sealed record EntityResponseComponentState : EntityComponentState, IGameplayResponseDeclarations
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<GameplayResponseDefinition>? Responses { get; init; }
}

public sealed record CardResponseComponentDefinition : CardComponentDefinition, IGameplayResponseDeclarations
{
    public GameplayResponseDefinition? Response { get; init; }
    [JsonIgnore] public ImmutableArray<GameplayResponseDefinition>? Responses => Response == null ? null : [Response];
}

/// <summary>Stage-five shadow sources are not yet executable content. The default publication path fails closed.</summary>
public static class GameplayResponseAuthoringGuard
{
    internal static bool Supports(Type type) => typeof(IGameplayResponseDeclarations).IsAssignableFrom(type) ||
        type == typeof(EntityDefinition) || type == typeof(CardContentDefinition) || type == typeof(CardComponentBundleDefinition) ||
        type == typeof(RunDefinition) || type == typeof(CardUpgradeDefinition);

    internal static bool ContainsDeclarations(object value) => Sources(value).Any(source =>
        source.Responses is { } definitions && (definitions.IsDefault || !definitions.IsEmpty));

    public static Result ValidateUnavailable(object value)
    {
        return ContainsDeclarations(value)
            ? Result.Failure("Gameplay responses are available for shadow compilation only; executable publication requires the dispatcher cutover")
            : Result.Success();
    }

    private static IEnumerable<IGameplayResponseDeclarations> Sources(object value) => value switch
        {
            IGameplayResponseDeclarations source => [source],
            EntityDefinition entity => entity.Components.OfType<IGameplayResponseDeclarations>(),
            CardContentDefinition card => card.Components.OfType<IGameplayResponseDeclarations>(),
            CardComponentBundleDefinition bundle => bundle.Components.OfType<IGameplayResponseDeclarations>(),
            RunDefinition run => run.MapNodes.OfType<IGameplayResponseDeclarations>(),
            CardUpgradeDefinition upgrade => upgrade.Patches.OfType<CardComponentPatchDefinition>().Select(item => item.Component)
                .Concat(upgrade.Patches.OfType<CardBundleSnapshotPatchDefinition>().SelectMany(item => item.Components))
                .OfType<IGameplayResponseDeclarations>(),
            CardUpgradeState upgrade => upgrade.Patches.OfType<CardComponentPatchDefinition>().Select(item => item.Component)
                .Concat(upgrade.Patches.OfType<CardBundleSnapshotPatchDefinition>().SelectMany(item => item.Components))
                .OfType<IGameplayResponseDeclarations>(),
            _ => []
        };
}
