using Core.Entity.Definitions;

namespace API.Models.Entities;

public sealed record EntityDefinitionDto
{
    public string DefinitionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string SpritePath { get; init; } = string.Empty;
    public IReadOnlyList<EntityComponentDefinition> Components { get; init; } = [];
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}
