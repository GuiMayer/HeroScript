using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public enum GameplayCommandRoute
{
    Run,
    StartEncounter,
    SandboxEncounter,
    ResolveEncounter,
    Combat
}

public sealed record GameplayCommandDescriptor(
    string Type,
    Type PayloadType,
    GameplayCommandRoute Route);

public sealed record GameplayCommandEnvelope(
    RunCommandIdentity Identity,
    JsonElement Payload);

public sealed record DecodedGameplayCommand(
    GameplayCommandEnvelope Envelope,
    GameplayCommandDescriptor Descriptor,
    object Payload);

public sealed record GameplayTransitionFrame
{
    public int FrameIndex { get; init; }
    public ulong Step { get; init; }
    public string Scope { get; init; } = "run";
    public string Kind { get; init; } = string.Empty;
    public JsonElement Resolution { get; init; }
}

public sealed record GameplayTransitionFact
{
    public int FactIndex { get; init; }
    public string Scope { get; init; } = "run";
    public string Type { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
}

/// <summary>
/// Pure candidate produced before persistence. A rejected plan never changes
/// either state reference and cannot publish frames or facts.
/// </summary>
public sealed record RunTransitionPlan
{
    private ImmutableArray<GameplayTransitionFrame> _frames = [];
    private ImmutableArray<GameplayTransitionFact> _facts = [];

    public required RunState PreviousState { get; init; }
    public required RunState CandidateState { get; init; }
    public required DeterministicContext Context { get; init; }
    public object? Value { get; init; }

    public IReadOnlyList<GameplayTransitionFrame> Frames
    {
        get => _frames;
        init => _frames = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<GameplayTransitionFact> Facts
    {
        get => _facts;
        init => _facts = value?.ToImmutableArray() ?? [];
    }
}

public sealed record GameplayCommandExecutionContext(
    RunCommandIdentity Identity,
    DeterministicContext Determinism);

public interface IRunCommandHandler
{
    GameplayCommandDescriptor Descriptor { get; }
    Result<RunTransitionPlan> Plan(
        RunState state,
        object payload,
        GameplayCommandExecutionContext context);
}

public interface IRunCommandHandler<TCommand> : IRunCommandHandler
{
    Result<RunTransitionPlan> Plan(
        RunState state,
        TCommand payload,
        GameplayCommandExecutionContext context);

    Result<RunTransitionPlan> IRunCommandHandler.Plan(
        RunState state,
        object payload,
        GameplayCommandExecutionContext context) =>
        payload is TCommand typed
            ? Plan(state, typed, context)
            : Result<RunTransitionPlan>.Failure(
                $"Handler for {Descriptor.Type} expected {typeof(TCommand).Name}, got {payload.GetType().Name}");
}

public sealed class RunCommandHandlerRegistry
{
    private readonly ImmutableDictionary<string, IRunCommandHandler> _handlers;

    public RunCommandHandlerRegistry(IEnumerable<IRunCommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var ordered = handlers
            .OrderBy(handler => handler.Descriptor.Type, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered
            .GroupBy(handler => handler.Descriptor.Type, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate command handler: {duplicate.Key}");

        _handlers = ordered.ToImmutableDictionary(
            handler => handler.Descriptor.Type,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<GameplayCommandDescriptor> Descriptors => _handlers.Values
        .Select(handler => handler.Descriptor)
        .OrderBy(descriptor => descriptor.Type, StringComparer.Ordinal)
        .ToArray();

    public Result<IRunCommandHandler> Resolve(string commandType) =>
        _handlers.TryGetValue(commandType, out var handler)
            ? Result<IRunCommandHandler>.Success(handler)
            : Result<IRunCommandHandler>.Failure($"Unsupported command type: {commandType}");
}

public interface IGameplayCommandCodec
{
    IReadOnlyList<GameplayCommandDescriptor> Descriptors { get; }
    Result<DecodedGameplayCommand> Decode(GameplayCommandEnvelope envelope);
}

/// <summary>
/// The only normalization and payload-validation boundary for gameplay
/// commands. Unknown command types and unmapped JSON properties fail closed.
/// </summary>
public sealed class GameplayCommandCodec : IGameplayCommandCodec
{
    private readonly ImmutableDictionary<string, GameplayCommandDescriptor> _descriptors;
    private readonly JsonSerializerOptions _jsonOptions;

    public GameplayCommandCodec(IEnumerable<GameplayCommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var ordered = descriptors
            .OrderBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered
            .GroupBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate command descriptor: {duplicate.Key}");

        _descriptors = ordered.ToImmutableDictionary(
            descriptor => descriptor.Type,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<GameplayCommandDescriptor> Descriptors => _descriptors.Values
        .OrderBy(descriptor => descriptor.Type, StringComparer.Ordinal)
        .ToArray();

    public Result<DecodedGameplayCommand> Decode(GameplayCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Identity);
        if (envelope.Identity.CommandId == Guid.Empty)
            return Result<DecodedGameplayCommand>.Failure("Command id is required");
        if (string.IsNullOrWhiteSpace(envelope.Identity.Type))
            return Result<DecodedGameplayCommand>.Failure("Command type is required");

        var type = envelope.Identity.Type.Trim().ToUpperInvariant();
        if (!_descriptors.TryGetValue(type, out var descriptor))
            return Result<DecodedGameplayCommand>.Failure($"Unsupported command type: {type}");

        var payload = envelope.Payload.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new { })
            : envelope.Payload.Clone();
        try
        {
            var decoded = payload.Deserialize(descriptor.PayloadType, _jsonOptions);
            if (decoded == null)
                return Result<DecodedGameplayCommand>.Failure($"Payload is required for {type}");
            var payloadHash = CanonicalJson.ComputeHash(payload);
            if (!string.IsNullOrWhiteSpace(envelope.Identity.PayloadHash) &&
                !string.Equals(envelope.Identity.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return Result<DecodedGameplayCommand>.Failure(
                    "Command payload hash does not match its canonical payload");
            }

            var normalized = envelope with
            {
                Identity = envelope.Identity with { Type = type, PayloadHash = payloadHash },
                Payload = payload
            };
            return Result<DecodedGameplayCommand>.Success(
                new DecodedGameplayCommand(normalized, descriptor, decoded));
        }
        catch (JsonException exception)
        {
            return Result<DecodedGameplayCommand>.Failure(
                $"Invalid payload for {type}: {exception.Message}");
        }
    }

    public static GameplayCommandCodec CreateDefault() => new(GameplayCommandDescriptors.All);
}

public static class GameplayCommandDescriptors
{
    private static GameplayCommandDescriptor Run<T>(string type) =>
        new(type, typeof(T), GameplayCommandRoute.Run);

    public static IReadOnlyList<GameplayCommandDescriptor> All { get; } =
    [
        Run<AdvanceNodeCommand>(RunCommandTypes.AdvanceNode),
        Run<ResolveNodeCommand>(RunCommandTypes.ResolveNode),
        Run<CountCommand>(RunCommandTypes.DrawCards),
        Run<CardIdsCommand>(RunCommandTypes.DiscardCards),
        Run<EmptyGameplayCommand>(RunCommandTypes.ShuffleDiscard),
        Run<CardSelectionCommand>(RunCommandTypes.CreateCardSelection),
        Run<CardSelectionCardsCommand>(RunCommandTypes.PickCardReward),
        Run<RerollCardSelectionCommand>(RunCommandTypes.RerollCardReward),
        Run<CardSelectionItemCommand>(RunCommandTypes.DecomposeCardReward),
        Run<ShopDefinitionCommand>(RunCommandTypes.CreateShop),
        Run<ShopItemCommand>(RunCommandTypes.BuyShopItem),
        Run<ShopCommand>(RunCommandTypes.RerollShop),
        Run<PreparationDefinitionCommand>(RunCommandTypes.CreatePreparation),
        Run<PreparationCommand>(RunCommandTypes.ApplyPreparationOption),
        Run<RelicCommand>(RunCommandTypes.AcquireRelic),
        Run<RelicInstanceCommand>(RunCommandTypes.RemoveRelic),
        Run<CardUpgradeCommand>(RunCommandTypes.UpgradeCard),
        Run<CheckpointCommand>(RunCommandTypes.RestoreCheckpoint),
        Run<ContentRevisionCommand>(RunCommandTypes.ActivateContentRevision),
        Run<RunResourceCommand>(RunCommandTypes.ApplyRunResource),
        Run<CardIdsCommand>(RunCommandTypes.AddCardsToHand),
        Run<MoveCardsCommand>(RunCommandTypes.MoveCards),
        new(RunCommandTypes.StartEncounter, typeof(StartEncounterCommand), GameplayCommandRoute.StartEncounter),
        new(GameplayCommandTypes.StartSandboxEncounter, typeof(StartSandboxEncounterCommand), GameplayCommandRoute.SandboxEncounter),
        new(RunCommandTypes.ResolveCombat, typeof(ResolveCombatCommand), GameplayCommandRoute.ResolveEncounter),
        new(GameplayCommandTypes.PlayCard, typeof(CombatGameplayCommand), GameplayCommandRoute.Combat),
        new(GameplayCommandTypes.ExecuteAction, typeof(CombatGameplayCommand), GameplayCommandRoute.Combat),
        new(GameplayCommandTypes.EndTurn, typeof(CombatGameplayCommand), GameplayCommandRoute.Combat)
    ];
}

public sealed record EmptyGameplayCommand;
public sealed record AdvanceNodeCommand(string TargetNodeId);
public sealed record ResolveNodeCommand(string CurrentNodeId);
public sealed record CountCommand(int Count);
public sealed record CardIdsCommand(IReadOnlyList<string> CardIds);
public sealed record CardSelectionCommand(string SelectionId);
public sealed record CardSelectionCardsCommand(Guid SelectionInstanceId, IReadOnlyList<string> CardIds);
public sealed record RerollCardSelectionCommand(Guid SelectionInstanceId, IReadOnlyList<string>? LockedCardIds = null);
public sealed record CardSelectionItemCommand(Guid SelectionInstanceId, string CardId);
public sealed record ShopItemCommand(Guid ShopInstanceId, string ItemId);
public sealed record ShopCommand(Guid ShopInstanceId);
public sealed record ShopDefinitionCommand(string ShopId);
public sealed record PreparationCommand(Guid PreparationInstanceId, string OptionId);
public sealed record PreparationDefinitionCommand(string PreparationId);
public sealed record RelicCommand(string RelicId);
public sealed record RelicInstanceCommand(Guid RelicInstanceId);
public sealed record CardUpgradeCommand(Guid CardInstanceId, string UpgradeId);
public sealed record CheckpointCommand(int Sequence);
public sealed record ContentRevisionCommand(string Revision);
public sealed record RunResourceCommand(
    string ResourceId,
    float Value,
    Core.Effects.ResourceEffectOperation Operation,
    Core.Resources.ResourceValueField Field = Core.Resources.ResourceValueField.Current);
public sealed record MoveCardsCommand(IReadOnlyList<string> CardIds, string Destination);
public sealed record StartEncounterCommand(
    CombatParticipantReference Hero,
    IReadOnlyList<CombatParticipantReference> Enemies,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? InitialResourceValues = null);
public sealed record StartSandboxEncounterCommand(
    CombatEntity Hero,
    IReadOnlyList<CombatEntity> Enemies,
    IReadOnlyDictionary<string, IReadOnlyList<Core.StatusEffects.StatusEffectInstance>>? InitialStatusEffects = null);
public sealed record ResolveCombatCommand(Guid CombatId = default);
public sealed record CombatGameplayCommand(
    string? ActorId = null,
    ActionType? ActionType = null,
    string? PowerId = null,
    string? TargetId = null,
    IReadOnlyList<string>? TargetIds = null,
    string? CostOptionId = null,
    Guid? CardInstanceId = null);
