using Core.Common;

namespace Core.Combat.Activation;

public interface ICombatActivationRulesLoader
{
    Result<CombatActivationRulesDefinition> Load(string configName, string rulesId);
}
