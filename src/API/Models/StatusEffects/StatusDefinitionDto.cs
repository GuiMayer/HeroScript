using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// DTO para representar uma definição de status effect (para GET)
/// </summary>
public class StatusDefinitionDto
{
    public string StatusId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsStackable { get; set; }
    public int? MaxStacks { get; set; }
    public string TickBehavior { get; set; } = string.Empty;
    public List<object> OnApply { get; set; } = new();
    public List<object> OnTick { get; set; } = new();
    public List<object> OnRemove { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
