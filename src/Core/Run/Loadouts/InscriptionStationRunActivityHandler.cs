using Core.Common;

namespace Core.Run.Loadouts;

internal sealed class InscriptionStationRunActivityHandler : RunActivityHandlerBase
{
    public override RunActivityType Type => RunActivityType.InscriptionStation;
    public override string? DefinitionKind => null;
    public override IReadOnlySet<string> CommandTypes { get; } = new HashSet<string>(StringComparer.Ordinal) { RunCommandTypes.ResolveNode };
    public override Result Validate(RunActivityDefinition activity) => string.IsNullOrWhiteSpace(activity.DefinitionId)
        ? Result.Failure("Inscription station activity requires a station ID") : Result.Success();
    public override bool IsComplete(RunState run, RunMapNodeState node) => run.Loadouts?.Offers?.Values.Any(offer =>
        offer.NodeId == node.NodeId && offer.StationId == node.Activity.DefinitionId && offer.Closed) == true;
    public override IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState run, RunMapNodeState node) =>
        IsComplete(run, node) || node.CompletionPolicy == RunActivityCompletionPolicy.Optional ? [Resolve(run, node)] : [];
}
