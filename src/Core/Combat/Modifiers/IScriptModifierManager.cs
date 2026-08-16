using Core.Common;

namespace Core.Combat.Modifiers;

public interface IScriptModifierManager
{
    Result LoadDefinitions(string configName);
    Result<ScriptModifierDefinition> GetDefinition(string modifierId);
    IReadOnlyList<ScriptModifierDefinition> GetAllDefinitions();
    Result<ScriptModifierInstance> ApplyModifier(string ownerId, string modifierId, int stacks = 1, int? duration = null, string? sourceId = null);
    Result<ScriptModifierInstance> ApplyModifier(Guid instanceId, string ownerId, string modifierId, int stacks = 1, int? duration = null, string? sourceId = null);
    Result RemoveModifier(string ownerId, Guid instanceId);
    IReadOnlyList<ScriptModifierInstance> GetActiveModifiers(string ownerId);
    Dictionary<string, float> GetPipelineModifiers(string ownerId, IEnumerable<string>? effectTags = null);
    Result TickDurations(string ownerId);
}
