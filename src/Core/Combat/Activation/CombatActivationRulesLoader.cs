using Core.Common;
using Core.Config;
using Core.Content;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Combat.Activation;

public sealed class CombatActivationRulesLoader : ICombatActivationRulesLoader, IRevisionedCombatActivationRulesLoader
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public CombatActivationRulesLoader(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _contentRuntimes = contentRuntimes;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<CombatActivationRulesDefinition> Load(string configName, string rulesId)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return Result<CombatActivationRulesDefinition>.Failure("Config name is required");
        if (string.IsNullOrWhiteSpace(rulesId))
            return Result<CombatActivationRulesDefinition>.Failure("Activation rules id is required");

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var resources = _resourceLoader.LoadResource($"combat-turn-rules/{rulesId}.json", chain);
            if (!resources.TryGetValue(rulesId, out var element))
                element = resources.Values.FirstOrDefault();

            if (element.ValueKind == JsonValueKind.Undefined)
                return Result<CombatActivationRulesDefinition>.Failure($"Activation rules not found: {rulesId}");

            var definition = JsonSerializer.Deserialize<CombatActivationRulesDefinition>(element.GetRawText(), _jsonOptions);
            if (definition == null)
                return Result<CombatActivationRulesDefinition>.Failure($"Invalid activation rules: {rulesId}");

            return Result<CombatActivationRulesDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<CombatActivationRulesDefinition>.Failure($"Failed to load activation rules {rulesId}: {ex.Message}");
        }
    }

    public Result<CombatActivationRulesDefinition> Load(
        string configName,
        string rulesId,
        string contentRevision)
    {
        if (_contentRuntimes == null)
            return Load(configName, rulesId);

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        return runtime.IsFailure
            ? Result<CombatActivationRulesDefinition>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<CombatActivationRulesDefinition>("activation-rules", rulesId);
    }
}
