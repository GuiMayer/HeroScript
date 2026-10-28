using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.GameplayEvents.Responses;

public sealed record GameplayResponseQuotaKey(string RegistrationId, ResponseFrequencyScope Scope, string Anchor);
public sealed record GameplayResponseReservation(GameplayResponseQuotas Quotas, bool Accepted, string? InvocationId);

/// <summary>Immutable transient counters for one root action. A rejected candidate never changes its input.</summary>
public sealed class GameplayResponseQuotas
{
    public const int MaximumInvocations = 4096;
    public GameplayEventScope Scope { get; }
    public int Count => _invocations.Count;
    private readonly ImmutableDictionary<GameplayResponseQuotaKey, int> _counts;
    private readonly ImmutableHashSet<string> _invocations;
    private readonly int _maximum;

    private GameplayResponseQuotas(GameplayEventScope scope, int maximum,
        ImmutableDictionary<GameplayResponseQuotaKey, int> counts, ImmutableHashSet<string> invocations)
        => (Scope, _maximum, _counts, _invocations) = (scope, maximum, counts, invocations);

    public static Result<GameplayResponseQuotas> Begin(GameplayEventScope scope, int maximumInvocations = MaximumInvocations)
    {
        if (scope == null || !new[] { scope.SettingId, scope.ContentRevision, scope.RunId, scope.BranchId, scope.ActionId, scope.CommandId }.All(EventValue.Valid) ||
            maximumInvocations is < 1 or > MaximumInvocations)
            return Result<GameplayResponseQuotas>.Failure("Response quotas require a bounded, explicit root action scope");
        return Result<GameplayResponseQuotas>.Success(new(scope, maximumInvocations,
            ImmutableDictionary<GameplayResponseQuotaKey, int>.Empty, ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
    }

    public Result<GameplayResponseReservation> Reserve(GameplayResponseCandidate candidate)
    {
        Result<GameplayResponseReservation> Fail(string message) => Result<GameplayResponseReservation>.Failure(message);
        if (candidate?.Occurrence == null || candidate.Registration?.Response?.Definition == null || candidate.Occurrence.Scope != Scope ||
            !EventValue.Valid(candidate.Occurrence.EventId) || !EventValue.Valid(candidate.Registration.RegistrationId))
            return Fail("Response reservation belongs to another root action");
        var frequency = candidate.Registration.Response.Definition.Frequency;
        if (frequency == null || frequency.Maximum is < 1 or > MaximumInvocations)
            return Fail("Response frequency is invalid");
        var anchor = frequency.Scope switch
        {
            ResponseFrequencyScope.Occurrence => candidate.Occurrence.EventId,
            ResponseFrequencyScope.Impact => candidate.Occurrence.ImpactId,
            ResponseFrequencyScope.Proc => candidate.Occurrence.ProcId,
            ResponseFrequencyScope.RootAction => Scope.ActionId,
            _ => null
        };
        if (anchor == null) return Fail("Response frequency requires an explicit supported causal anchor");
        var id = CanonicalJson.ComputeHash(new { Scope, candidate.Occurrence.EventId, candidate.Registration.RegistrationId });
        var key = new GameplayResponseQuotaKey(candidate.Registration.RegistrationId, frequency.Scope, anchor);
        var count = _counts.GetValueOrDefault(key);
        if (_invocations.Contains(id) || count >= frequency.Maximum)
            return Result<GameplayResponseReservation>.Success(new(this, false, null));
        if (Count == _maximum) return Fail("Response invocation budget exceeded");
        var next = new GameplayResponseQuotas(Scope, _maximum, _counts.SetItem(key, count + 1), _invocations.Add(id));
        return Result<GameplayResponseReservation>.Success(new(next, true, id));
    }
}
