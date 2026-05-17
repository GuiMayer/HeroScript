using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Entity.Controllers;

/// <summary>
/// Controller para companions controlados por sistema Gambit.
/// </summary>
public class GambitController : IEntityController
{
    public string ControllerId { get; }
    public EntityControllerType Type => EntityControllerType.GAMBIT;

    private readonly IGambitEngine _gambitEngine;
    private readonly IReadOnlyList<string>? _gambitIds;

    public GambitController(string controllerId = "gambit_controller")
        : this(new EmptyGambitEngine(), controllerId)
    {
    }

    public GambitController(
        IGambitEngine gambitEngine,
        string controllerId = "gambit_controller",
        IReadOnlyList<string>? gambitIds = null)
    {
        ControllerId = controllerId;
        _gambitEngine = gambitEngine ?? throw new ArgumentNullException(nameof(gambitEngine));
        _gambitIds = gambitIds;
    }
    
    /// <summary>
    /// Avalia gambits e decide ação.
    /// </summary>
    public Task<Result<EntityAction>> DecideAction(
        Entity controlledEntity,
        CombatState combatState)
    {
        return Task.FromResult(_gambitEngine.DecideAction(controlledEntity, combatState, _gambitIds));
    }
    
    public void OnTurnStart(Entity entity, CombatState state)
    {
    }
    
    public void OnTurnEnd(Entity entity, CombatState state)
    {
    }
    
    public void OnDamageTaken(Entity entity, float damage)
    {
    }
    
    public void OnDamageDealt(Entity entity, float damage)
    {
    }

    private sealed class EmptyGambitEngine : IGambitEngine
    {
        public Result LoadDefinitions(string configName) => Result.Success();

        public Result<GambitDefinition> GetDefinition(string gambitId) =>
            Result<GambitDefinition>.Failure($"Gambit definition not found: {gambitId}");

        public IReadOnlyList<GambitDefinition> GetAllDefinitions() => Array.Empty<GambitDefinition>();

        public Result<EntityAction> DecideAction(Entity controlledEntity, CombatState combatState, IEnumerable<string>? gambitIds = null) =>
            Result<EntityAction>.Success(new EntityAction { ActionType = ActionType.PASS });
    }
}
