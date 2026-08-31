using Core.Combat.Gambits;
using Core.Entity.Controllers;

namespace API.Models.Gambits;

public class GambitDefinitionResponse
{
    public string GambitId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public List<GambitCondition> Conditions { get; set; } = new();
    public GambitActionDefinition Action { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public static GambitDefinitionResponse FromDefinition(GambitDefinition definition)
    {
        return new GambitDefinitionResponse
        {
            GambitId = definition.GambitId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Priority = definition.Priority,
            Conditions = definition.Conditions.ToList(),
            Action = definition.Action,
            Tags = definition.Tags.ToList()
        };
    }
}

public class GambitDecisionResponse
{
    public string EntityId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? PowerId { get; set; }
    public string? TargetId { get; set; }
    public int? CostOptionId { get; set; }

    public static GambitDecisionResponse FromAction(string entityId, EntityAction action)
    {
        return new GambitDecisionResponse
        {
            EntityId = entityId,
            ActionType = action.ActionType.ToString(),
            PowerId = action.PowerId,
            TargetId = action.TargetId,
            CostOptionId = action.CostOptionId
        };
    }
}
