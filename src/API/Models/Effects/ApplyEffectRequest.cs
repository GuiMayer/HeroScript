using API.Models.Actions;

namespace API.Models.Effects;

public class ApplyEffectRequest
{
    public string Scope { get; set; } = "RUN";
    public Guid? CombatId { get; set; }
    public string? RunId { get; set; }
    public string SourceEntityId { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public string? SourceActionId { get; set; }
    public string? SourceCardId { get; set; }
    public EffectDefinitionDto Effect { get; set; } = new();
}
