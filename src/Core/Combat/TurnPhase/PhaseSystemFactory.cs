using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Events;
using Core.Logging;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Factory para criar instâncias completas do sistema de fases.
/// Facilita a configuração e inicialização de diferentes estilos de TCG.
/// </summary>
public class PhaseSystemFactory
{
    private readonly ILogger _logger;
    private readonly IEventBus? _eventBus;
    private readonly PhaseSequenceLoader _loader;
    private readonly string _configName;
    
    public PhaseSystemFactory(ILogger logger, IConfigManager configManager, IResourceLoader resourceLoader, IEventBus? eventBus = null, string configName = "default")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
        _loader = new PhaseSequenceLoader(logger, configManager, resourceLoader);
        _configName = string.IsNullOrWhiteSpace(configName) ? "default" : configName;
    }
    
    /// <summary>
    /// Cria um sistema de fases completo a partir de uma sequência configurada em JSON.
    /// </summary>
    /// <param name="sequenceId">Identificador da sequência em phase-sequences/{sequenceId}.json</param>
    /// <returns>Sistema de fases configurado</returns>
    public Result<PhaseSystem> CreateFromResource(string sequenceId)
    {
        var loadResult = _loader.LoadFromResource(sequenceId, _configName);
        if (loadResult.IsFailure)
        {
            return Result<PhaseSystem>.Failure($"Failed to load configuration: {loadResult.Error}");
        }
        
        return CreateSystem(loadResult.Value);
    }
    
    /// <summary>
    /// Cria um sistema de fases completo a partir de uma string JSON.
    /// </summary>
    /// <param name="json">String JSON de configuração</param>
    /// <returns>Sistema de fases configurado</returns>
    public Result<PhaseSystem> CreateFromJson(string json)
    {
        // Carregar sequência
        var loadResult = _loader.LoadFromJson(json);
        if (loadResult.IsFailure)
        {
            return Result<PhaseSystem>.Failure($"Failed to load configuration: {loadResult.Error}");
        }
        
        return CreateSystem(loadResult.Value);
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo Magic: The Gathering.
    /// </summary>
    /// <returns>Sistema de fases estilo Magic</returns>
    public Result<PhaseSystem> CreateMagicStyle()
    {
        return CreateFromPreset("magic-style");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo Yu-Gi-Oh!.
    /// </summary>
    /// <returns>Sistema de fases estilo Yu-Gi-Oh!</returns>
    public Result<PhaseSystem> CreateYuGiOhStyle()
    {
        return CreateFromPreset("yugioh-style");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo Hearthstone.
    /// </summary>
    /// <returns>Sistema de fases estilo Hearthstone</returns>
    public Result<PhaseSystem> CreateHearthstoneStyle()
    {
        return CreateFromPreset("hearthstone-style");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo clássico/simples.
    /// </summary>
    /// <returns>Sistema de fases estilo clássico</returns>
    public Result<PhaseSystem> CreateClassicStyle()
    {
        return CreateFromPreset("classic-style");
    }
    
    /// <summary>
    /// Cria um sistema de fases sem fases (retrocompatibilidade).
    /// Permite que o combate funcione sem sistema de fases ativo.
    /// </summary>
    /// <returns>Sistema de fases desabilitado</returns>
    public Result<PhaseSystem> CreateDisabled()
    {
        var sequence = new PhaseSequenceDefinition
        {
            Name = "Disabled",
            Description = "No phase system - backward compatibility mode",
            Version = "1.0.0",
            Phases = new List<TurnPhase> { TurnPhase.NONE },
            PhaseDetails = new Dictionary<TurnPhase, PhaseDefinition>
            {
                {
                    TurnPhase.NONE,
                    new PhaseDefinition
                    {
                        Name = "No Phases",
                        Description = "Phase system disabled",
                        AllowedActions = new List<ActionType>(),
                        ValidNextPhases = new List<TurnPhase>(),
                        AutoTransition = false,
                        AllowPriority = false
                    }
                }
            },
            AllowPhaseSkipping = false
        };
        
        return CreateSystem(sequence);
    }
    
    private Result<PhaseSystem> CreateFromPreset(string presetFileName)
    {
        return CreateFromResource(presetFileName);
    }
    
    private Result<PhaseSystem> CreateSystem(PhaseSequenceDefinition sequence)
    {
        try
        {
            // Criar componentes do sistema
            var prioritySystem = new PrioritySystem(_logger);
            var phaseManager = new PhaseManager(prioritySystem, _logger, _eventBus);
            var stackManager = new ActionStackManager(_logger, _eventBus);
            
            var system = new PhaseSystem
            {
                Sequence = sequence,
                PrioritySystem = prioritySystem,
                PhaseManager = phaseManager,
                StackManager = stackManager
            };
            
            _logger.LogInformation($"Created phase system: {sequence.Name}");
            
            return Result<PhaseSystem>.Success(system);
        }
        catch (Exception ex)
        {
            return Result<PhaseSystem>.Failure($"Error creating phase system: {ex.Message}");
        }
    }
}

/// <summary>
/// Sistema de fases completo com todos os componentes necessários.
/// </summary>
public record PhaseSystem
{
    /// <summary>
    /// Definição da sequência de fases
    /// </summary>
    public PhaseSequenceDefinition Sequence { get; init; } = null!;
    
    /// <summary>
    /// Sistema de prioridade
    /// </summary>
    public IPrioritySystem PrioritySystem { get; init; } = null!;
    
    /// <summary>
    /// Gerenciador de fases
    /// </summary>
    public IPhaseManager PhaseManager { get; init; } = null!;
    
    /// <summary>
    /// Gerenciador de pilha de ações
    /// </summary>
    public IActionStackManager StackManager { get; init; } = null!;
}
