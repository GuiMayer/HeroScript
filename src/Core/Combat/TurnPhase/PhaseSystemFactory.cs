using Core.Combat.Models;
using Core.Common;
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
    
    public PhaseSystemFactory(ILogger logger, IEventBus? eventBus = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
        _loader = new PhaseSequenceLoader(logger);
    }
    
    /// <summary>
    /// Cria um sistema de fases completo a partir de um arquivo de configuração.
    /// </summary>
    /// <param name="configPath">Caminho para o arquivo JSON de configuração</param>
    /// <returns>Sistema de fases configurado</returns>
    public Result<PhaseSystem> CreateFromFile(string configPath)
    {
        // Carregar sequência
        var loadResult = _loader.LoadFromFile(configPath);
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
        return CreateFromPreset("magic-style.json");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo Yu-Gi-Oh!.
    /// </summary>
    /// <returns>Sistema de fases estilo Yu-Gi-Oh!</returns>
    public Result<PhaseSystem> CreateYuGiOhStyle()
    {
        return CreateFromPreset("yugioh-style.json");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo Hearthstone.
    /// </summary>
    /// <returns>Sistema de fases estilo Hearthstone</returns>
    public Result<PhaseSystem> CreateHearthstoneStyle()
    {
        return CreateFromPreset("hearthstone-style.json");
    }
    
    /// <summary>
    /// Cria um sistema de fases pré-configurado para estilo clássico/simples.
    /// </summary>
    /// <returns>Sistema de fases estilo clássico</returns>
    public Result<PhaseSystem> CreateClassicStyle()
    {
        return CreateFromPreset("classic-style.json");
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
        // Construir caminho para o preset
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var presetPath = Path.Combine(baseDir, "Core", "Combat", "TurnPhase", "Configurations", presetFileName);
        
        // Se não encontrar no baseDir, tentar caminho relativo
        if (!File.Exists(presetPath))
        {
            presetPath = Path.Combine("src", "Core", "Combat", "TurnPhase", "Configurations", presetFileName);
        }
        
        if (!File.Exists(presetPath))
        {
            return Result<PhaseSystem>.Failure($"Preset configuration not found: {presetFileName}");
        }
        
        return CreateFromFile(presetPath);
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
