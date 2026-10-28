using Core.GameplayEvents;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Meta;
using Core.Resources;

namespace Core.Run.Loadouts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InscriptionOfferSelection { Random, Directed }

public sealed record InscriptionStationDefinition
{
    public string StationId { get; init; } = string.Empty;
    public ImmutableArray<string> NodeIds { get; init; } = [];
    public ImmutableSortedDictionary<string, int> Pool { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
    public InscriptionOfferSelection Selection { get; init; }
    public int OfferCount { get; init; } = 3;
    public int MaximumPurchases { get; init; } = 1;
    public ImmutableArray<ResourceAmount> Costs { get; init; } = [];
}

public sealed record InscriptionOfferState
{
    public Guid OfferId { get; init; }
    public string StationId { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public ImmutableArray<string> Options { get; init; } = [];
    public ImmutableArray<string> PurchasedIds { get; init; } = [];
    public ImmutableArray<ResourceAmount> Costs { get; init; } = [];
    public int MaximumPurchases { get; init; }
    public bool Closed { get; init; }
}

public sealed record OpenInscriptionOfferCommand(string ActorId, string StationId);
public sealed record ChooseInscriptionOfferCommand(Guid OfferId, string? InscriptionId);

/// <summary>One acquisition authority for rewards, priced shops and limited directed crafting.</summary>
public static class InscriptionStationTransitions
{
    public static Result Validate(ImmutableArray<InscriptionStationDefinition> stations, ImmutableArray<string> allowed)
    {
        if (stations.IsDefault || stations.Length > 64 || stations.Any(station => station == null) ||
            stations.Select(station => station.StationId).Distinct(StringComparer.Ordinal).Count() != stations.Length)
            return Result.Failure("Inscription stations require unique IDs and a bounded catalog");
        foreach (var station in stations)
            if (string.IsNullOrWhiteSpace(station.StationId) || station.NodeIds.IsDefaultOrEmpty || station.NodeIds.Any(string.IsNullOrWhiteSpace) ||
                station.NodeIds.Distinct(StringComparer.Ordinal).Count() != station.NodeIds.Length || !Enum.IsDefined(station.Selection) ||
                station.Pool.Count is < 1 or > 512 || station.Pool.Any(pair => !allowed.Contains(pair.Key) || pair.Value is < 1 or > 1000000) ||
                station.OfferCount is < 1 or > 32 || station.MaximumPurchases is < 1 or > 16 || station.Costs.IsDefault ||
                station.Costs.Length > 32 || station.Costs.Any(cost => cost == null || string.IsNullOrWhiteSpace(cost.ResourceId) || !float.IsFinite(cost.Amount) || cost.Amount < 0))
                return Result.Failure("Invalid inscription station nodes, weights, limits or prices");
        return Result.Success();
    }

    public static bool IsAvailable(RunState run, InscriptionStationDefinition station) => run.Loadouts != null &&
        run.Lifecycle == RunLifecycleState.Active && run.ActiveEncounterId == null && run.CurrentNodeId != null &&
        station.NodeIds.Contains(run.CurrentNodeId) && !run.Map.ResolvedNodeIds.Contains(run.CurrentNodeId) &&
        run.Map.Nodes.Any(node => node.NodeId == run.CurrentNodeId) &&
        !(run.Loadouts.Offers?.Values.Any(offer => offer.NodeId == run.CurrentNodeId && offer.StationId == station.StationId) ?? false);

    public static Result<RunState> Open(RunState run, ContentRuntime runtime, OpenInscriptionOfferCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ActorId) || string.IsNullOrWhiteSpace(command.StationId))
            return Result<RunState>.Failure("Inscription recipient and station are required");
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        var station = (policy.Value.Stations ?? []).FirstOrDefault(value => value.StationId == command.StationId);
        if (station == null || !IsAvailable(run, station) || !run.Loadouts!.Actors.ContainsKey(command.ActorId))
            return Result<RunState>.Failure("Inscription station or recipient is unavailable at this node");
        if (run.Loadouts.Offers?.Count >= 512) return Result<RunState>.Failure("Inscription offer history limit reached");
        var remaining = station.Pool.Where(pair => RunEligibility.Allows(run, UnlockTargetKind.Inscription, pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        var context = run.Determinism;
        var options = ImmutableArray.CreateBuilder<string>();
        var count = station.Selection == InscriptionOfferSelection.Directed ? remaining.Count : station.OfferCount;
        while (options.Count < count && remaining.Count > 0)
        {
            var index = 0;
            if (station.Selection == InscriptionOfferSelection.Random)
            {
                var draw = context.DrawInt32(checked(remaining.Sum(pair => pair.Value)));
                context = draw.Context;
                var cursor = draw.Value;
                while ((cursor -= remaining[index].Value) >= 0) index++;
            }
            options.Add(remaining[index].Key); remaining.RemoveAt(index);
        }
        if (options.Count == 0 && run.Eligibility?.EmptyPools == EmptyEligiblePoolPolicy.Reject)
            return Result<RunState>.Failure("Inscription station has no eligible candidates");
        var allocated = context.AllocateId("inscription-offer");
        var offer = new InscriptionOfferState { OfferId = allocated.Value, StationId = station.StationId, NodeId = run.CurrentNodeId!,
            ActorId = command.ActorId, ContentRevision = runtime.Manifest.Revision, Options = options.ToImmutable(),
            Costs = station.Costs, MaximumPurchases = station.MaximumPurchases,
            Fingerprint = CanonicalJson.ComputeHash(new { station, options = options.ToImmutable(), eligibility = run.Eligibility?.EffectiveFingerprint,
                revision = runtime.Manifest.Revision, source = run.Determinism }) };
        return Result<RunState>.Success(run with { Loadouts = run.Loadouts! with
            { Offers = (run.Loadouts.Offers ?? ImmutableSortedDictionary<Guid, InscriptionOfferState>.Empty).Add(offer.OfferId, offer) },
            Determinism = allocated.Context.AdvanceStep() });
    }

    public static Result<RunState> Choose(RunState run, ContentRuntime runtime, ChooseInscriptionOfferCommand command) => ChooseCore(run, runtime, command, null);

    internal static Result<RunState> ChooseObserved(RunState run, ContentRuntime runtime, ChooseInscriptionOfferCommand command, GameplayRunObservation? observation) =>
        ChooseCore(run, runtime, command, observation);

    private static Result<RunState> ChooseCore(RunState run, ContentRuntime runtime, ChooseInscriptionOfferCommand command, GameplayRunObservation? observation)
    {
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        if (run.Loadouts?.Offers is not { } offers || !offers.TryGetValue(command.OfferId, out var offer) || offer == null || offer.Closed ||
            run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null || run.CurrentNodeId != offer.NodeId ||
            run.Map.ResolvedNodeIds.Contains(offer.NodeId) || runtime.Manifest.Revision != offer.ContentRevision)
            return Result<RunState>.Failure("Inscription offer is closed, stale or belongs to another node");
        var next = run;
        var selected = offer.PurchasedIds;
        if (command.InscriptionId is { } id)
        {
            if (!offer.Options.Contains(id) || selected.Contains(id) || selected.Length >= offer.MaximumPurchases)
                return Result<RunState>.Failure("Inscription is not an available choice in this captured offer");
            var spent = RunResourceTransitions.Pay(run, offer.Costs, $"inscription:{offer.OfferId}:{id}", observation);
            if (spent.IsFailure) return Result<RunState>.Failure(spent.Error);
            run = spent.Value.Run;
            var granted = InscriptionAcquisition.Grant(run with { ResourceState = spent.Value.State }, runtime, offer.ActorId, id,
                $"station:{offer.NodeId}:{offer.StationId}:{offer.Fingerprint}");
            if (granted.IsFailure) return granted;
            next = granted.Value; selected = selected.Add(id);
        }
        var updated = offer with { PurchasedIds = selected, Closed = command.InscriptionId == null ||
            selected.Length >= offer.MaximumPurchases || selected.Length >= offer.Options.Length };
        return Result<RunState>.Success(next with { Loadouts = next.Loadouts! with { Offers = next.Loadouts!.Offers!.SetItem(offer.OfferId, updated) },
            Determinism = next.Determinism.AdvanceStep() });
    }
}
