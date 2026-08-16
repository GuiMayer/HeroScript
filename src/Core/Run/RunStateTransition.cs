namespace Core.Run;

/// <summary>
/// Pure result returned by a run submodule: the replacement aggregate and the
/// operation-specific value exposed by the application facade.
/// </summary>
public sealed record RunStateTransition<T>(RunState State, T Value);
