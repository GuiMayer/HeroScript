namespace API.Models.Modifiers;

public class ApplyModifierRequest
{
    public string OwnerId { get; set; } = string.Empty;
    public string ModifierId { get; set; } = string.Empty;
    public int Stacks { get; set; } = 1;
    public int? Duration { get; set; }
    public string? SourceId { get; set; }
}
