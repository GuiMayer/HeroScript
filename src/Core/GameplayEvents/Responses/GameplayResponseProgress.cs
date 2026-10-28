namespace Core.GameplayEvents.Responses;

/// <summary>Private immutable candidate continuation. Speculative forks never share mutable budgets.</summary>
internal sealed record GameplayResponseProgress(GameplayResponseQuotas Quotas, int Work, int MaximumInvocations);
