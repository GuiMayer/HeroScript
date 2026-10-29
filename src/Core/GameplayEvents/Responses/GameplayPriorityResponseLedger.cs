using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.GameplayEvents.Responses;

/// <summary>Immutable counters attached to the existing priority continuation, never a second action queue.</summary>
public sealed record GameplayPriorityResponseLedger
{
    public string WindowId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string RootRunId { get; init; } = string.Empty;
    public string CombatId { get; init; } = string.Empty;
    public ImmutableSortedDictionary<string, int> Counts { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);

    internal static Result<(CombatState State, bool Accepted)> Reserve(CombatState state, GameplayResponseCandidate candidate)
    {
        Result<(CombatState, bool)> Fail(string message) => Result<(CombatState, bool)>.Failure(message);
        if (candidate.Registration.Response.Definition.Frequency.Scope != ResponseFrequencyScope.PriorityWindow)
            return Result<(CombatState, bool)>.Success((state, true));
        var captured = candidate.Snapshot.Combat?.PriorityWindow;
        if (captured == null) return Fail("Priority-window frequency requires an active captured priority window");
        var scope = candidate.Occurrence.Scope;
        var ledger = state.PriorityWindow?.Responses ?? captured.Responses ?? new GameplayPriorityResponseLedger
        {
            WindowId = captured.WindowId, SettingId = scope.SettingId, ContentRevision = scope.ContentRevision,
            RootRunId = scope.RunId, CombatId = state.CombatId.ToString("D")
        };
        if (!EventValue.Valid(ledger.WindowId) || ledger.SettingId != scope.SettingId || ledger.ContentRevision != scope.ContentRevision ||
            ledger.RootRunId != scope.RunId || ledger.CombatId != state.CombatId.ToString("D") || ledger.Counts == null ||
            ledger.Counts.Count > GameplayResponseQuotas.MaximumInvocations || ledger.Counts.Any(pair => !EventValue.Valid(pair.Key) || pair.Value is < 1 or > GameplayResponseQuotas.MaximumInvocations))
            return Fail("Priority response counters do not match their captured continuation");
        var key = candidate.Registration.RegistrationId;
        var count = ledger.Counts.GetValueOrDefault(key);
        if (count >= candidate.Registration.Response.Definition.Frequency.Maximum)
            return Result<(CombatState, bool)>.Success((state, false));
        if (count == 0 && ledger.Counts.Count == GameplayResponseQuotas.MaximumInvocations)
            return Fail("Priority response counter budget exceeded");
        // A terminal fact may observe the last closed window. There is no remaining continuation to retain.
        var next = state.PriorityWindow == null ? state : state with
        { PriorityWindow = state.PriorityWindow with { Responses = ledger with { Counts = ledger.Counts.SetItem(key, checked(count + 1)) } } };
        return Result<(CombatState, bool)>.Success((next, true));
    }
}
