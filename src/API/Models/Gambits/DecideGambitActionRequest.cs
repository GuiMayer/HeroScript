namespace API.Models.Gambits;

public class DecideGambitActionRequest
{
    public string EntityId { get; set; } = string.Empty;
    public Guid CombatId { get; set; }
    public List<string>? GambitIds { get; set; }
}
